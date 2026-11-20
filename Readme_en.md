# TaskList

[简体中文](Readme.md)

A Wunderlist-like task list app for Windows. It works entirely offline and stores tasks and settings locally.

## Features

- Quickly add or edit tasks with notes, list assignment, and light, medium, or heavy task weights.
- View tasks created today, all open tasks, or a specific list; customize list names and colors.
- Complete tasks and undo the most recent completion; track daily and weekly completions and your day streak.
- Review completed tasks by day, reopen tasks, and generate daily reports to copy to the clipboard.
- Customize completion sounds and celebration effects; import MP3 files up to 10 seconds long.
- Switch instantly between Simplified Chinese and English, with system tray and single-instance support.

## Run and Build

Requires **Windows and the .NET 8 SDK**. Run these commands from the project root:

```powershell
# Run in Debug mode
dotnet run --project TaskList.csproj -c Debug

# Build the Release version
dotnet build TaskList.csproj -c Release
```

Stack: C#, WPF, and SQLite (`Microsoft.Data.Sqlite`), with WinForms for the system tray.

## Usage Notes

- Choose a language under **Settings → Language**. Chinese is the default; changes apply immediately and are saved automatically.
- Task titles, notes, and custom list names are not automatically translated.
- **Today** shows open tasks created today, rather than tasks filtered by a due date.
- Closing or minimizing the window hides the app in the system tray. Select **Exit TaskList** from the tray menu to quit.

## Local Data

| Configuration | Database location |
| --- | --- |
| Debug | `TaskList.db` in the project root |
| Release | `%LOCALAPPDATA%\TaskList\TaskList.db` |

Imported sounds are stored in the `Sounds` folder within the same data directory. Before backing up, exit the app from the tray, then copy the database and the `Sounds` folder. Debug and Release use separate data directories.
