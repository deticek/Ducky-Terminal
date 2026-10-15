# Ducky Terminal

             __             __             __             __     
         ___( o)>       ___( o)>       ___( o)>       ___( o)>   
         \ <_. )        \ <_. )        \ <_. )        \ <_. )
          `---'          `---'          `---'          `---'     

Ducky is a desktop terminal application built with Avalonia and .NET 10. It is designed to run on Windows, Linux, and macOS.

## Features

- Split the terminal into panes and switch between workspaces.
- Choose a theme, font, and shell prompt.
- Customize the interface and behavior in `Config/config.json`.
- Add terminal commands as small C# console projects. The `hello` command is a sample; `password` is also included.
- Uses shells available on the computer. Windows supports Command Prompt and PowerShell; Linux and macOS use Bash.

## Run from source

Install the .NET 10 SDK, then run these commands from the project directory:

```powershell
dotnet restore
dotnet run
```

## Build self-contained versions

On Windows, run the publish script from the project directory:

```powershell
.\publish-all.ps1
```

The script creates self-contained builds for Windows x64 and ARM64, Linux x64 and ARM64, and macOS Intel and Apple Silicon. Every build includes the .NET runtime, so the target computer does not need .NET installed. The builds are placed inside the project in `relese/<runtime>`:

```text
relese/
├── win-x64/
├── win-arm64/
├── linux-x64/
├── linux-arm64/
├── osx-x64/
└── osx-arm64/
```

On Windows, start `Ducky.exe` from the folder for your architecture. On Linux or macOS, run `sh Ducky.sh` from the matching folder. The `relese` folder is ignored by Git; these finished builds can be uploaded separately as release downloads.

## Configuration

In a published build, Ducky stores its settings in your user profile so it can save them regardless of where the application is installed. On Windows, the file is `%LOCALAPPDATA%\Ducky\Config\config.json`. The shipped themes and custom command programs remain in the application folder (`Themes` and `Config/Commands`). When running from source, settings are read from `Config/config.json` in the project.

## Keyboard shortcuts

While you are in ducky just press F1 for list of keyboard shortcuts
