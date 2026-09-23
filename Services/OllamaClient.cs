using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeepLocal.Services
{
    public sealed record OllamaModel(
        string Name,
        long Size,
        string Family,
        string ParameterSize,
        bool IsCloud,
        IReadOnlyList<string> Capabilities)
    {
        public bool CanGenerate => Capabilities.Count == 0 || Capabilities.Contains("completion");
        public bool CanThink => Capabilities.Contains("thinking");
    }

    public readonly record struct PullProgress(string Status, long Total, long Completed)
    {
        public double Percent => Total > 0 ? Math.Min(100.0, Completed * 100.0 / Total) : 0;
    }

    public readonly record struct GenChunk(string Response, bool IsThinking);

    /// <summary>Errore di Ollama con il messaggio originale del server (es. "model not found").</summary>
    public sealed class OllamaException : Exception
    {
        public int StatusCode { get; }
        public OllamaException(int status, string message) : base(message) { StatusCode = status; }
        public bool IsModelMissing => StatusCode == 404 || Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
        public bool IsUnauthorized => StatusCode == 401 || StatusCode == 403;
    }

    public sealed class OllamaClient
    {
        public string Host { get; }

        // Nessun timeout globale: i modelli grandi possono impiegare minuti a caricarsi.
        // Ogni chiamata ha il proprio CancellationToken.
        private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

        private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

        public OllamaClient(string host = "http://127.0.0.1:11434")
        {
            Host = host.TrimEnd('/');
        }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                using var resp = await Http.GetAsync($"{Host}/api/version", cts.Token);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return false;
            }
        }

        /// <summary>Modelli installati, con le capacità lette da /api/show (per escludere gli embedding).</summary>
        public async Task<List<OllamaModel>> ListModelsAsync(CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            using var resp = await Http.GetAsync($"{Host}/api/tags", cts.Token);
            await ThrowIfErrorAsync(resp, cts.Token);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));

            var raw = new List<(string Name, long Size, string Family, string Params, bool Cloud)>();
            if (doc.RootElement.TryGetProperty("models", out var models))
            {
                foreach (var m in models.EnumerateArray())
                {
                    var name = m.GetProperty("name").GetString() ?? "";
                    if (name.Length == 0) continue;
                    long size = m.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var l) ? l : 0;
                    string family = "", pars = "";
                    if (m.TryGetProperty("details", out var d))
                    {
                        family = d.TryGetProperty("family", out var f) ? f.GetString() ?? "" : "";
                        pars = d.TryGetProperty("parameter_size", out var p) ? p.GetString() ?? "" : "";
                    }
                    bool cloud = m.TryGetProperty("remote_host", out var rh) && !string.IsNullOrEmpty(rh.GetString())
                                 || name.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase)
                                 || name.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase);
                    raw.Add((name, size, family, pars, cloud));
                }
            }

            var tasks = raw.Select(async r =>
                new OllamaModel(r.Name, r.Size, r.Family, r.Params, r.Cloud, await CapabilitiesAsync(r.Name, cts.Token)));
            return (await Task.WhenAll(tasks)).ToList();
        }

        private async Task<IReadOnlyList<string>> CapabilitiesAsync(string model, CancellationToken ct)
        {
            try
            {
                using var content = JsonContent(new { model });
                using var resp = await Http.PostAsync($"{Host}/api/show", content, ct);
                if (!resp.IsSuccessStatusCode) return Array.Empty<string>();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                    return caps.EnumerateArray().Select(c => c.GetString() ?? "").Where(c => c.Length > 0).ToList();
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            return Array.Empty<string>();
        }

        /// <summary>Scarica un modello (ollama pull). Se annullato, Ollama riprende dal punto raggiunto.</summary>
        public async IAsyncEnumerable<PullProgress> PullAsync(string model, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var line in PostStreamAsync("/api/pull", new { model, stream = true }, ct))
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err))
                    throw new OllamaException(500, err.GetString() ?? "pull failed");
                var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                long total = root.TryGetProperty("total", out var t) && t.TryGetInt64(out var tv) ? tv : 0;
                long done = root.TryGetProperty("completed", out var c) && c.TryGetInt64(out var cv) ? cv : 0;
                yield return new PullProgress(status, total, done);
            }
        }

        /// <summary>Generazione in streaming: restituisce i pezzi di testo man mano che arrivano.</summary>
        public async IAsyncEnumerable<GenChunk> GenerateAsync(
            string model, string prompt, bool canThink, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var payload = BuildGeneratePayload(model, prompt, canThink, stream: true, maxTokens: null);
            await foreach (var line in PostStreamAsync("/api/generate", payload, ct))
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err))
                    throw new OllamaException(500, err.GetString() ?? "generation failed");
                if (root.TryGetProperty("thinking", out var th) && !string.IsNullOrEmpty(th.GetString()))
                    yield return new GenChunk("", IsThinking: true);
                var piece = root.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "";
                if (piece.Length > 0) yield return new GenChunk(piece, IsThinking: false);
                if (root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True) yield break;
            }
        }

        /// <summary>Generazione breve senza streaming (rilevamento della lingua).</summary>
        public async Task<string> GenerateOnceAsync(string model, string prompt, bool canThink, int maxTokens, CancellationToken ct)
        {
            var payload = BuildGeneratePayload(model, prompt, canThink, stream: false, maxTokens: maxTokens);
            using var content = JsonContent(payload);
            using var resp = await Http.PostAsync($"{Host}/api/generate", content, ct);
            await ThrowIfErrorAsync(resp, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "";
        }

        private static Dictionary<string, object?> BuildGeneratePayload(string model, string prompt, bool canThink, bool stream, int? maxTokens)
        {
            var options = new Dictionary<string, object?> { ["temperature"] = 0.1 };
            if (maxTokens is int n) options["num_predict"] = n;

            var payload = new Dictionary<string, object?>
            {
                ["model"] = model,
                ["prompt"] = prompt,
                ["stream"] = stream,
                ["keep_alive"] = "15m",
                ["options"] = options,
            };

            // I modelli che "ragionano" (deepseek-r1, qwen3, gpt-oss) per tradurre non ne hanno bisogno:
            // spento dove si può, al minimo su gpt-oss che non lo permette.
            if (canThink)
                payload["think"] = model.StartsWith("gpt-oss", StringComparison.OrdinalIgnoreCase) ? "low" : false;

            return payload;
        }

        private async IAsyncEnumerable<string> PostStreamAsync(string path, object payload, [EnumeratorCancellation] CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Host + path) { Content = JsonContent(payload) };
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            await ThrowIfErrorAsync(resp, ct);

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) yield break;
                if (line.Length > 0) yield return line;
            }
        }

        private static async Task ThrowIfErrorAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            if (resp.IsSuccessStatusCode) return;
            string message = resp.ReasonPhrase ?? "HTTP " + (int)resp.StatusCode;
            try
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var e) && e.GetString() is { Length: > 0 } msg)
                    message = msg;
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            throw new OllamaException((int)resp.StatusCode, message);
        }

        private static StringContent JsonContent(object payload) =>
            new(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");

        /// <summary>Percorso dell'app desktop di Ollama, se installata nel percorso standard.</summary>
        public static string? FindOllamaApp()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var p in new[]
            {
                Path.Combine(local, "Programs", "Ollama", "ollama app.exe"),
                Path.Combine(local, "Programs", "Ollama", "ollama.exe"),
            })
            {
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}
