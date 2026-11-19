using Microsoft.Data.Sqlite;
using TaskList.Models;

namespace TaskList.Data;

public static class TaskRepository
{
    private const string SelectTask = """
        SELECT t.Id, t.GroupId, t.Title, t.Notes, t.Weight, t.CreatedAt,
               t.CompletedAt, t.SortOrder, g.Name, g.Color
        FROM Tasks t JOIN Groups g ON g.Id = t.GroupId
        """;

    // ---------- 清单 ----------

    public static List<TaskGroup> GetGroups()
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Color, IsInbox, SortOrder FROM Groups ORDER BY IsInbox DESC, SortOrder, Id;";
        using var r = cmd.ExecuteReader();
        var result = new List<TaskGroup>();
        while (r.Read())
        {
            result.Add(new TaskGroup
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                Color = r.GetString(2),
                IsInbox = r.GetInt32(3) != 0,
                SortOrder = r.GetInt32(4)
            });
        }
        return result;
    }

    public static Dictionary<long, int> GetOpenCounts()
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT GroupId, COUNT(*) FROM Tasks WHERE CompletedAt IS NULL GROUP BY GroupId;";
        using var r = cmd.ExecuteReader();
        var result = new Dictionary<long, int>();
        while (r.Read()) result[r.GetInt64(0)] = r.GetInt32(1);
        return result;
    }

    public static long GetInboxId()
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Groups WHERE IsInbox = 1 LIMIT 1;";
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public static long AddGroup(string name, string color)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Groups (Name, Color, IsInbox, SortOrder, CreatedAt)
            VALUES ($name, $color, 0, (SELECT IFNULL(MAX(SortOrder), 0) + 1 FROM Groups), $now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$color", color);
        cmd.Parameters.AddWithValue("$now", Database.ToDb(DateTime.Now));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static void RenameGroup(long id, string name, string color)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE Groups SET Name = $name, Color = $color WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$color", color);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteGroup(long id)
    {
        using var c = Database.Open();
        using var tx = c.BeginTransaction();
        long inboxId = GetInboxId();

        using (var move = c.CreateCommand())
        {
            move.Transaction = tx;
            move.CommandText = "UPDATE Tasks SET GroupId = $inbox WHERE GroupId = $id;";
            move.Parameters.AddWithValue("$inbox", inboxId);
            move.Parameters.AddWithValue("$id", id);
            move.ExecuteNonQuery();
        }

        using (var remove = c.CreateCommand())
        {
            remove.Transaction = tx;
            remove.CommandText = "DELETE FROM Groups WHERE Id = $id AND IsInbox = 0;";
            remove.Parameters.AddWithValue("$id", id);
            remove.ExecuteNonQuery();
        }

        tx.Commit();
    }

    // ---------- 任务 ----------

    public static List<TaskItem> GetOpenTasks(long? groupId, bool todayOnly)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        var sql = SelectTask + " WHERE t.CompletedAt IS NULL";
        if (groupId.HasValue)
        {
            sql += " AND t.GroupId = $group";
            cmd.Parameters.AddWithValue("$group", groupId.Value);
        }
        if (todayOnly)
        {
            sql += " AND substr(t.CreatedAt, 1, 10) = $day";
            cmd.Parameters.AddWithValue("$day", DateTime.Today.ToString(Database.DayFormat));
        }
        sql += " ORDER BY t.Id DESC;";
        cmd.CommandText = sql;

        using var r = cmd.ExecuteReader();
        return ReadAll(r);
    }

    public static List<TaskItem> GetCompletedOn(DateTime day)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectTask + " WHERE substr(t.CompletedAt, 1, 10) = $day ORDER BY t.CompletedAt ASC;";
        cmd.Parameters.AddWithValue("$day", day.ToString(Database.DayFormat));
        using var r = cmd.ExecuteReader();
        return ReadAll(r);
    }

    public static List<DateTime> GetCompletedDates()
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT substr(CompletedAt, 1, 10) AS Day FROM Tasks
            WHERE CompletedAt IS NOT NULL ORDER BY Day DESC;
            """;
        using var r = cmd.ExecuteReader();
        var result = new List<DateTime>();
        while (r.Read()) result.Add(Database.FromDay(r.GetString(0)));
        return result;
    }

    public static long AddTask(TaskItem task)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Tasks (GroupId, Title, Notes, Weight, CreatedAt, CompletedAt, SortOrder)
            VALUES ($group, $title, $notes, $weight, $created, NULL, 0);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$group", task.GroupId);
        cmd.Parameters.AddWithValue("$title", task.Title);
        cmd.Parameters.AddWithValue("$notes", task.Notes);
        cmd.Parameters.AddWithValue("$weight", (int)task.Weight);
        cmd.Parameters.AddWithValue("$created", Database.ToDb(task.CreatedAt));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static void UpdateTask(TaskItem task)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE Tasks SET GroupId = $group, Title = $title, Notes = $notes, Weight = $weight
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$group", task.GroupId);
        cmd.Parameters.AddWithValue("$title", task.Title);
        cmd.Parameters.AddWithValue("$notes", task.Notes);
        cmd.Parameters.AddWithValue("$weight", (int)task.Weight);
        cmd.Parameters.AddWithValue("$id", task.Id);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteTask(long id)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM Tasks WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void CompleteTask(long id, DateTime completedAt)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE Tasks SET CompletedAt = $at WHERE Id = $id AND CompletedAt IS NULL;";
        cmd.Parameters.AddWithValue("$at", Database.ToDb(completedAt));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void ReopenTask(long id)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE Tasks SET CompletedAt = NULL WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static TaskItem? GetTask(long id)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectTask + " WHERE t.Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadTask(r) : null;
    }

    // ---------- 统计 / 设置 ----------

    public static TaskStats GetStats()
    {
        var today = DateTime.Today;
        var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);

        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM Tasks WHERE CompletedAt IS NOT NULL AND substr(CompletedAt,1,10) = $today),
              (SELECT COUNT(*) FROM Tasks WHERE CompletedAt IS NOT NULL AND CompletedAt >= $week),
              (SELECT COUNT(*) FROM Tasks WHERE CompletedAt IS NULL);
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString(Database.DayFormat));
        cmd.Parameters.AddWithValue("$week", Database.ToDb(monday));

        int todayCount = 0, weekCount = 0, openCount = 0;
        using (var r = cmd.ExecuteReader())
        {
            if (r.Read())
            {
                todayCount = r.GetInt32(0);
                weekCount = r.GetInt32(1);
                openCount = r.GetInt32(2);
            }
        }

        return new TaskStats(todayCount, weekCount, openCount, ComputeStreak(c));
    }

    private static int ComputeStreak(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT substr(CompletedAt, 1, 10) AS Day FROM Tasks
            WHERE CompletedAt IS NOT NULL ORDER BY Day DESC;
            """;
        var days = new HashSet<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) days.Add(r.GetString(0));

        var cursor = DateTime.Today;
        if (!days.Contains(cursor.ToString(Database.DayFormat))) cursor = cursor.AddDays(-1);

        int streak = 0;
        while (days.Contains(cursor.ToString(Database.DayFormat)))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }

    public static string GetSetting(string key, string fallback = "")
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string ?? fallback;
    }

    public static void SetSetting(string key, string value)
    {
        using var c = Database.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private static List<TaskItem> ReadAll(SqliteDataReader r)
    {
        var result = new List<TaskItem>();
        while (r.Read()) result.Add(ReadTask(r));
        return result;
    }

    private static TaskItem ReadTask(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        GroupId = r.GetInt64(1),
        Title = r.GetString(2),
        Notes = r.GetString(3),
        Weight = (TaskWeight)r.GetInt32(4),
        CreatedAt = Database.FromDb(r.GetString(5)),
        CompletedAt = r.IsDBNull(6) ? null : Database.FromDb(r.GetString(6)),
        SortOrder = r.GetInt32(7),
        GroupName = r.GetString(8),
        GroupColor = r.GetString(9)
    };
}
