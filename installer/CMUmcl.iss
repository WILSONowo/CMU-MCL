; CMUmcl .NET48 installer.
; Build with scripts/build-net48-installer.ps1.
; The package directory must contain the full client payload, not user data.

#ifndef CMUMCL_VERSION
  #error CMUMCL_VERSION is required
#endif
#ifndef CMUMCL_PACKAGE_DIR
  #error CMUMCL_PACKAGE_DIR is required
#endif
#ifndef CMUMCL_INSTALLER_OUT
  #error CMUMCL_INSTALLER_OUT is required
#endif
#ifndef CMUMCL_INSTALLER_NAME
  #error CMUMCL_INSTALLER_NAME is required
#endif

[Setup]
AppId={{2F6B9E4A-7C3D-4B8E-9A1F-6D2C8E5B7A31}
AppName=CMUmcl
AppVersion={#CMUMCL_VERSION}
AppVerName=CMUmcl {#CMUMCL_VERSION}
AppPublisher=CMUmcl
AppPublisherURL=https://github.com/yuhui258036/CMU-MCL
AppSupportURL=https://github.com/yuhui258036/CMU-MCL
DefaultDirName={autopf}\CMUmcl
DefaultGroupName=CMUmcl
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#CMUMCL_INSTALLER_OUT}
OutputBaseFilename={#CMUMCL_INSTALLER_NAME}
LicenseFile=..\LICENSE
SetupIconFile=..\CMUmcl\Assets\CMUmcl.ico
UninstallDisplayIcon={app}\CMUmcl.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#CMUMCL_PACKAGE_DIR}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "sandbox-data\*,test-data\*,.update-backups\*,*.pdb"

[Icons]
Name: "{group}\CMUmcl"; Filename: "{app}\CMUmcl.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall CMUmcl"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CMUmcl"; Filename: "{app}\CMUmcl.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\CMUmcl.exe"; Description: "Launch CMUmcl"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    and (Release >= 528040);
  if not Result then
    MsgBox('CMUmcl requires .NET Framework 4.8 or later. Please install it and run setup again.',
      mbError, MB_OK);
end;
