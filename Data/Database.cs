using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace TaskList.Data;

public static class Database
{
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    public const string DayFormat = "yyyy-MM-dd";

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskList");

    public static string DbPath => Path.Combine(DataDirectory, "TaskList.db");

    private static string ConnectionString => $"Data Source={DbPath};Cache=Shared";

    public static string ToDb(DateTime value) => value.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static DateTime FromDb(string value)
        => DateTime.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);

    public static DateTime FromDay(string value)
        => DateTime.ParseExact(value, DayFormat, CultureInfo.InvariantCulture);

    public static SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public static void Initialize()
    {
        Directory.CreateDirectory(DataDirectory);
        using var connection = Open();

        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Groups (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                Name      TEXT    NOT NULL,
                Color     TEXT    NOT NULL DEFAULT '#6C63FF',
                IsInbox   INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT    NOT NULL
            );
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Tasks (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                GroupId     INTEGER NOT NULL REFERENCES Groups(Id) ON DELETE CASCADE,
                Title       TEXT    NOT NULL,
                Notes       TEXT    NOT NULL DEFAULT '',
                Weight      INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL,
                CompletedAt TEXT    NULL,
                SortOrder   INTEGER NOT NULL DEFAULT 0
            );
            """);
        Execute(connection, "CREATE INDEX IF NOT EXISTS IX_Tasks_CompletedAt ON Tasks(CompletedAt);");
        Execute(connection, "CREATE INDEX IF NOT EXISTS IX_Tasks_GroupId ON Tasks(GroupId);");
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Settings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """);

        using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO Groups (Name, Color, IsInbox, SortOrder, CreatedAt)
            SELECT '收件箱', '#6C63FF', 1, 0, $now
            WHERE NOT EXISTS (SELECT 1 FROM Groups WHERE IsInbox = 1);
            """;
        seed.Parameters.AddWithValue("$now", ToDb(DateTime.Now));
        seed.ExecuteNonQuery();

        SetDefaultSetting(connection, "SoundEnabled", "1");
        SetDefaultSetting(connection, "TrayTipShown", "0");
    }

    private static void SetDefaultSetting(SqliteConnection connection, string key, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Settings (Key, Value) VALUES ($key, $value);";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
