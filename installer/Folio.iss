#ifndef AppKey
  #define AppKey "Folio"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#define AppVersion GetFileVersion(SourceDir + "\Folio.exe")

[Setup]
AppId={#AppKey}-PDF-Workspace
AppName={#AppKey}
AppVersion={#AppVersion}
AppPublisher=Folio contributors
AppPublisherURL=https://github.com/AidedPolecat6/Folio
AppSupportURL=https://github.com/AidedPolecat6/Folio/issues
DefaultDirName={localappdata}\Programs\{#AppKey}
DefaultGroupName={#AppKey}
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\Assets\Folio.ico
UninstallDisplayIcon={app}\Folio.exe
LicenseFile=..\LICENSE
OutputDir=..\artifacts\installer
OutputBaseFilename={#AppKey}-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "danish"; MessagesFile: "compiler:Languages\Danish.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppKey}"; Filename: "{app}\Folio.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppKey}"; Filename: "{app}\Folio.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\{#AppKey}.Pdf"; ValueType: string; ValueData: "Folio PDF document"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#AppKey}.Pdf\DefaultIcon"; ValueType: string; ValueData: """{app}\Folio.exe"",0"
Root: HKCU; Subkey: "Software\Classes\{#AppKey}.Pdf\shell\open\command"; ValueType: string; ValueData: """{app}\Folio.exe"" ""%1"""
Root: HKCU; Subkey: "Software\{#AppKey}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppKey}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#AppKey}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "A focused PDF workspace with tabs, bookmarks and annotations"
Root: HKCU; Subkey: "Software\{#AppKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "{#AppKey}.Pdf"
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppKey}"; ValueData: "Software\{#AppKey}\Capabilities"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "{#AppKey}.Pdf"; ValueData: ""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Folio.exe"; Description: "Launch Folio"; Flags: nowait postinstall skipifsilent
Filename: "ms-settings:defaultapps?registeredAppUser={#AppKey}"; Description: "Choose Folio as your default PDF app in Windows Settings"; Flags: shellexec postinstall unchecked skipifsilent

; User preferences in LocalAppData\Folio are deliberately retained on uninstall.
