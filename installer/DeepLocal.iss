; DeepLocal, script Inno Setup 6
; Un solo script per i due installer: build.ps1 lo compila con /DLang=en e /DLang=it.
; Stesso AppId della v1.0: l'installer aggiorna la versione esistente invece di affiancarla.

#ifndef Lang
  #define Lang "en"
#endif
#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif

#define AppName "DeepLocal"
#define AppPublisher "MetaDarko"
#define AppURL "https://github.com/ShinRalexis/DeepLocal"
#define AppExe "DeepLocal.exe"
#define PublishDir "..\publish\win-x64"

#if Lang == "it"
  #define TxtOtherTasks "Altre opzioni:"
  #define TxtStartup "Avvia DeepLocal con Windows (resta nella tray, Alt+T sempre pronto)"
  #define TxtOllamaMissing "Ollama non risulta installato su questo PC.%n%nDeepLocal traduce usando i modelli di Ollama: installalo da ollama.com, poi apri DeepLocal e scarica TranslateGemma 12B dal menu dei modelli.%n%nVuoi aprire adesso la pagina di download di Ollama?"
  #define TxtComment "Traduttore offline con Ollama"
#else
  #define TxtOtherTasks "Other options:"
  #define TxtStartup "Start DeepLocal with Windows (stays in the tray, Alt+T always ready)"
  #define TxtOllamaMissing "Ollama does not seem to be installed on this PC.%n%nDeepLocal translates with Ollama models: install it from ollama.com, then open DeepLocal and download TranslateGemma 12B from the model menu.%n%nOpen the Ollama download page now?"
  #define TxtComment "Offline translator powered by Ollama"
#endif

[Setup]
AppId={{A3B4B3F8-6E1C-4B3B-9C2A-5AD53A8F7B10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
AppCopyright=Copyright (c) 2025-2026 {#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
#if Lang == "it"
OutputBaseFilename=DeepLocal_Setup_ITA
#else
OutputBaseFilename=DeepLocal_Setup_EN
#endif
SetupIconFile=..\Assets\DeepLocal_UserIcon_Framed.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern
CloseApplications=force
RestartApplications=no
UsedUserAreasWarning=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
ShowLanguageDialog=no

[Languages]
#if Lang == "it"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
#else
Name: "en"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{#TxtStartup}"; GroupDescription: "{#TxtOtherTasks}"; Flags: unchecked

[InstallDelete]
; Collegamento di avvio della v1.0 (senza --minimized): si ricrea sotto se l'opzione è scelta
Type: files; Name: "{userstartup}\DeepLocal.lnk"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
; Lingua dell'interfaccia scelta con l'installer (letta da Settings.cs)
Filename: "{app}\setup.ini"; Section: "Setup"; Key: "UiLanguage"; String: "{#Lang}"; Flags: uninsdeletesection

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#TxtComment}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#TxtComment}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--minimized"; Comment: "{#TxtComment}"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{userstartup}\DeepLocal.lnk"
Type: files; Name: "{app}\setup.ini"

[Registry]
; Voce di avvio scritta dall'interruttore nelle impostazioni dell'app: si toglie disinstallando
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "DeepLocal"; Flags: uninsdeletevalue

[Code]
function OllamaInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{localappdata}\Programs\Ollama\ollama.exe'))
         or FileExists(ExpandConstant('{autopf}\Ollama\ollama.exe'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep = ssPostInstall) and (not WizardSilent()) and (not OllamaInstalled()) then
  begin
    if MsgBox('{#TxtOllamaMissing}', mbInformation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://ollama.com/download/windows', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;
