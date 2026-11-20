using System.ComponentModel;
using System.Globalization;
using System.Windows.Markup;
using TaskList.Data;

namespace TaskList.Services;

public sealed record LocalizedMessage(string Key, params object?[] Arguments)
{
    public override string ToString() => LocalizationService.T(Key, Arguments);
}

public sealed class LocalizedException(LocalizedMessage message) : InvalidOperationException
{
    public LocalizedMessage LocalizedMessage { get; } = message;
    public override string Message => LocalizedMessage.ToString();
}

public sealed record AppLanguage(string Code, string Name);

public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();
    public static IReadOnlyList<AppLanguage> Languages { get; } = new[]
    {
        new AppLanguage("zh-CN", "中文（简体）"),
        new AppLanguage("en-US", "English")
    };

    private string _languageCode = "zh-CN";
    private long _inboxId;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string LanguageCode => _languageCode;
    public XmlLanguage UiLanguage => XmlLanguage.GetLanguage(_languageCode);
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Instance._languageCode);
    public static bool IsEnglish => Instance._languageCode == "en-US";

    private LocalizationService() { }

    public void Initialize()
    {
        _inboxId = TaskRepository.GetInboxId();
        ApplyLanguage(TaskRepository.GetSetting("Language", "zh-CN"));
    }

    public void SetLanguage(string code)
    {
        if (code is not ("zh-CN" or "en-US")) return;
        if (_languageCode == code) return;
        TaskRepository.SetSetting("Language", code);
        ApplyLanguage(code);
    }

    private void ApplyLanguage(string code)
    {
        _languageCode = code == "en-US" ? "en-US" : "zh-CN";
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public static string GroupName(long groupId, string originalName)
        => groupId == Instance._inboxId && Instance._inboxId != 0 ? T("收件箱") : originalName;

    public static string T(string key, params object?[] arguments)
    {
        var text = IsEnglish && English.TryGetValue(key, out var translated) ? translated : key;
        return arguments.Length == 0 ? text : string.Format(Culture, text, arguments);
    }

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["TaskList · 专注今天"] = "TaskList · Focus on today",
        ["离线"] = "Offline",
        ["最小化到托盘"] = "Minimize to tray",
        ["最大化 / 还原"] = "Maximize / Restore",
        ["关闭到托盘"] = "Close to tray",
        ["今日进度"] = "Today's progress",
        ["今日完成"] = "Done today",
        ["待办任务"] = "Open tasks",
        ["连续天数"] = "Day streak",
        ["今天"] = "Today",
        ["昨天"] = "Yesterday",
        ["前天"] = "Two days ago",
        ["全部任务"] = "All tasks",
        ["历史记录"] = "History",
        ["我的清单"] = "My lists",
        ["新建清单"] = "New list",
        ["所有清单"] = "All lists",
        ["查看所有清单中的任务"] = "View tasks from all lists",
        ["重命名清单"] = "Rename list",
        ["删除清单"] = "Delete list",
        ["设置"] = "Settings",
        ["音效与完成特效设置"] = "Sound and celebration settings",
        ["数据仅保存在本机 · 完全离线"] = "Stored locally · Fully offline",
        ["撤销"] = "Undo",
        ["详细新建"] = "New with details",
        ["输入任务内容后按 Enter 添加"] = "Type a task and press Enter to add",
        ["轻量"] = "Light",
        ["轻型"] = "Light",
        ["中型"] = "Medium",
        ["重型"] = "Heavy",
        ["轻"] = "L",
        ["中"] = "M",
        ["重"] = "H",
        ["添加"] = "Add",
        ["编辑任务"] = "Edit task",
        ["删除任务"] = "Delete task",
        ["这里空空的"] = "Nothing here yet",
        ["任务标题"] = "Task title",
        ["备注（可选）"] = "Notes (optional)",
        ["任务类型"] = "Task weight",
        ["所属清单"] = "List",
        ["清单名称"] = "List name",
        ["标识颜色"] = "List color",
        ["取消"] = "Cancel",
        ["创建于"] = "Created",
        ["恢复为待办"] = "Reopen task",
        ["删除记录"] = "Delete record",
        ["永久删除这条完成记录"] = "Permanently delete this completed task",
        ["把每一次完成，变成属于你的庆祝"] = "Make every completed task a celebration of your own",
        ["语言"] = "Language",
        ["界面语言"] = "App language",
        ["切换后立即生效，设置会自动保存。"] = "Changes take effect immediately and are saved automatically.",
        ["完成音效"] = "Completion sounds",
        ["为轻型、中型、重型任务分别选择一段声音"] = "Choose a sound for light, medium, and heavy tasks",
        ["开启完成音效"] = "Enable completion sounds",
        ["开启或关闭完成音效"] = "Turn completion sounds on or off",
        ["选择 MP3"] = "Choose MP3",
        ["试听"] = "Preview",
        ["恢复默认"] = "Reset",
        ["仅支持不超过 10 秒的 MP3；导入后移动或删除原文件不影响使用。"] = "MP3 files up to 10 seconds only. Moving or deleting the original after import will not affect playback.",
        ["试听不受总开关影响，新声音会替换当前声音。"] = "Previews work even when sounds are off. A new sound replaces the current one.",
        ["停止试听"] = "Stop preview",
        ["完成特效"] = "Celebration effects",
        ["统一使用所选风格，任务越重，庆祝越隆重"] = "Use your chosen style for all tasks, with bigger celebrations for heavier tasks",
        ["原有分级特效"] = "Classic effects",
        ["从轻快纸屑到成就卡片，再到全屏庆祝，保留熟悉的完成仪式。"] = "Keep the familiar celebration: confetti, achievement cards, and full-screen cheers.",
        ["鼓掌特效"] = "Applause effect",
        ["为你鼓掌"] = "A round of applause",
        ["拍合的双手、喝彩波纹与缤纷彩带，每一件完成都值得被看见。"] = "Clapping hands, ripples, and colorful ribbons. Every completed task deserves recognition.",
        ["极简勾选特效"] = "Minimal check effect",
        ["极简勾选"] = "Minimal check",
        ["一笔勾选、轻盈回弹与细波纹，利落确认，不打断你的专注。"] = "A crisp check, a gentle bounce, and soft ripples, without interrupting your focus.",
        ["完成印章特效"] = "Completion stamp effect",
        ["完成印章"] = "Completion stamp",
        ["深红圆印、仿篆刻字形与斑驳印泥，盖下属于这件事的收官印痕。"] = "A deep red seal with engraved lettering and textured ink marks a job well done.",
        ["星光汇聚特效"] = "Starlight effect",
        ["星光汇聚"] = "Starlight",
        ["逐颗点亮星座、描出连线，最后亮起金色主星，让完成拥有自己的光芒。"] = "Stars light up, a constellation takes shape, and a golden star celebrates your achievement.",
        ["礼花绽放特效"] = "Fireworks effect",
        ["礼花绽放"] = "Fireworks",
        ["两朵、四朵、六朵礼花分波升空，双层绽放与明亮长拖尾，为完成隆重喝彩。"] = "Waves of two, four, or six fireworks bloom with bright trails to celebrate your achievement.",
        ["预览强度"] = "Preview weight",
        ["预览特效"] = "Preview effect",
        ["预览只展示视觉效果，不会完成任务或产生历史记录。"] = "Preview shows the effect only. It does not complete tasks or create history records.",
        ["取消导入"] = "Cancel import",
        ["音频保存目录（备份时请一起复制）："] = "Sound folder (include it in your backup):",
        ["完成!"] = "Done!",
        ["完成 · 中型任务"] = "Done · Medium task",
        ["稳稳推进了一步"] = "One steady step forward",
        ["完成 · 送给努力的你"] = "Done · You earned this",
        ["任务完成"] = "Task completed",
        ["收件箱"] = "Inbox",
        ["今天创建的待办 · 一件一件，慢慢来"] = "Tasks created today · One step at a time",
        ["所有清单中尚未完成的任务"] = "Open tasks from all your lists",
        ["汇总每个清单里尚未完成的任务"] = "Open tasks across every list",
        ["这个清单里的待办任务"] = "Open tasks in this list",
        ["个性化音效与完成特效"] = "Customize sounds and celebration effects",
        ["按天回看已经完成的任务"] = "Look back at completed tasks, day by day",
        ["今天还没有新建任务，从上面添加第一件吧"] = "No tasks created today. Add your first one above",
        ["这个清单是空的，添加一件想做的事"] = "This list is empty. Add something you'd like to do",
        ["所有清单里都没有待办，享受这份清爽"] = "No open tasks in any list. Enjoy the clear space",
        ["没有未完成的任务，享受这份清爽"] = "No open tasks. Enjoy the clear space",
        ["今日完成 {0}   ·   本周 {1}   ·   连续 {2} 天"] = "Done today {0}   ·   This week {1}   ·   {2}-day streak",
        ["今日已完成 {0} 项 · 待办 {1} 项"] = "Done today: {0} · Open: {1}",
        ["已完成「{0}」"] = "Completed \"{0}\"",
        ["确定删除「{0}」吗？删除后无法恢复。"] = "Delete \"{0}\"? This cannot be undone.",
        ["编辑清单"] = "Edit list",
        ["确定删除清单「{0}」吗？其中的任务会移动到收件箱，不会丢失。"] = "Delete the list \"{0}\"? Its tasks will be moved to Inbox, not deleted.",
        ["保存"] = "Save",
        ["新建任务"] = "New task",
        ["创建任务"] = "Create task",
        ["保存修改"] = "Save changes",
        ["创建清单"] = "Create list",
        ["确认删除"] = "Delete",
        ["yyyy年M月d日"] = "MMMM d, yyyy",
        ["M月d日 HH:mm"] = "MMM d HH:mm",
        ["今天 {0}"] = "Today {0}",
        ["昨天 {0}"] = "Yesterday {0}",
        ["{0} · 创建于 {1}"] = "{0} · Created {1}",
        ["{0} 天前"] = "{0} days ago",
        ["这一天还没有完成的任务"] = "No tasks completed on this day",
        ["共 {0} 项   ·   轻量 {1} / 中型 {2} / 重型 {3}"] = "{0} tasks   ·   Light {1} / Medium {2} / Heavy {3}",
        ["工作日报 · {0}（{1}）"] = "Daily report · {0} ({1})",
        ["（这一天没有已完成的任务）"] = "(No tasks completed on this day)",
        ["{0}. [{1}] {2}（{3} · {4} 完成）{5}"] = "{0}. [{1}] {2} ({3} · completed {4}){5}",
        ["合计 {0} 项：轻量 {1} / 中型 {2} / 重型 {3}"] = "Total: {0} tasks — Light {1} / Medium {2} / Heavy {3}",
        ["已复制到剪贴板"] = "Copied to clipboard",
        ["删除完成记录"] = "Delete completed task",
        ["确定删除「{0}」的完成记录吗？删除后无法恢复。"] = "Delete the completion record for \"{0}\"? This cannot be undone.",
        ["内置音效"] = "Built-in sound",
        ["{0} 秒 · 已复制到应用数据目录"] = "{0} seconds · Copied to the app data folder",
        ["设置自动保存，下一次完成任务时生效。"] = "Settings are saved automatically and apply to the next completed task.",
        ["轻型任务"] = "Light tasks",
        ["中型任务"] = "Medium tasks",
        ["重型任务"] = "Heavy tasks",
        ["内置：轻快的双音提示"] = "Built-in: a light two-note chime",
        ["内置：明亮的三音和弦"] = "Built-in: a bright three-note chord",
        ["内置：饱满的庆祝旋律"] = "Built-in: a rich celebration melody",
        ["正在试听{0}：{1}"] = "Previewing {0}: {1}",
        ["完成音效已开启。"] = "Completion sounds are on.",
        ["完成音效已关闭，视觉特效仍会播放。"] = "Completion sounds are off. Visual effects will still play.",
        ["已选择鼓掌特效。"] = "Applause effect selected.",
        ["已选择极简勾选特效。"] = "Minimal check effect selected.",
        ["已选择完成印章特效。"] = "Completion stamp effect selected.",
        ["已选择星光汇聚特效。"] = "Starlight effect selected.",
        ["已选择礼花绽放特效。"] = "Fireworks effect selected.",
        ["已选择原有分级特效。"] = "Classic effects selected.",
        ["{0}的音频文件已丢失，播放时会使用内置音效。"] = "The sound file for {0} is missing. The built-in sound will be used.",
        ["{0}的音频配置无效，已使用内置音效。"] = "The sound configuration for {0} is invalid. Using the built-in sound.",
        ["选择{0}的完成音效（不超过 10 秒）"] = "Choose a completion sound for {0} (up to 10 seconds)",
        ["MP3 音频 (*.mp3)|*.mp3"] = "MP3 audio (*.mp3)|*.mp3",
        ["请选择 MP3 文件。"] = "Please choose an MP3 file.",
        ["正在复制并检查 MP3，原音效配置暂时保持不变…"] = "Copying and checking the MP3. Your current sound settings are unchanged…",
        ["已导入{0}音效：{1}（{2:0.##} 秒）。"] = "Imported sound for {0}: {1} ({2:0.##} seconds).",
        ["导入已取消，原音效配置保持不变。"] = "Import canceled. Your current sound settings are unchanged.",
        ["导入失败：{0}"] = "Import failed: {0}",
        ["{0}已恢复内置音效。"] = "Restored the built-in sound for {0}.",
        ["恢复失败：{0}"] = "Reset failed: {0}",
        ["为每一次完成喝彩"] = "Celebrate every completed task",
        ["特效预览失败：{0}"] = "Effect preview failed: {0}",
        ["设置保存失败：{0}"] = "Could not save settings: {0}",
        ["无法读取这段音频的时长，请选择其他 MP3。"] = "Cannot read the audio duration. Please choose another MP3.",
        ["无法解码这段 MP3，请确认文件有效且系统媒体组件可用。"] = "Cannot decode this MP3. Check that the file is valid and the system media components are available.",
        ["请选择时长大于 0 且不超过 10 秒的 MP3。此文件未导入。"] = "Choose an MP3 longer than 0 and no more than 10 seconds. This file was not imported.",
        ["读取 MP3 超时，请选择其他文件或检查系统媒体组件。"] = "Reading the MP3 timed out. Choose another file or check the system media components.",
        ["自定义音频文件不存在，已使用内置音效。请在设置中重新导入。"] = "The custom sound file is missing. Using the built-in sound. Please import it again in Settings.",
        ["自定义音频无效或超过 10 秒，已使用内置音效。"] = "The custom sound is invalid or exceeds 10 seconds. Using the built-in sound.",
        ["自定义 MP3 无法播放，已使用内置音效。"] = "Cannot play the custom MP3. Using the built-in sound.",
        ["自定义 MP3 无法播放，已使用内置音效。请在设置中重新导入。"] = "Cannot play the custom MP3. Using the built-in sound. Please import it again in Settings.",
        ["自定义 MP3 无法打开，已使用内置音效。"] = "Cannot open the custom MP3. Using the built-in sound.",
        ["内置音效无法播放，请检查系统音频组件。"] = "Cannot play the built-in sound. Please check the system audio components.",
        ["完成特效播放失败：{0}"] = "Celebration effect failed: {0}",
        ["本地数据库初始化失败：\n{0}"] = "Could not initialize the local database:\n{0}",
        ["发生未处理的错误：\n{0}"] = "An unexpected error occurred:\n{0}",
        ["无法获取 Debug 版的工程数据目录。"] = "Cannot locate the project data folder for the Debug build.",
        ["显示主窗口"] = "Show TaskList",
        ["快速添加任务"] = "Quick add task",
        ["退出 TaskList"] = "Exit TaskList",
        ["TaskList 已在后台运行"] = "TaskList is running in the background",
        ["关闭或最小化都会缩到托盘，双击图标即可回来。"] = "Closing or minimizing hides TaskList in the tray. Double-click its icon to return.",
        ["无法加载 TaskList 主图标。"] = "Cannot load the TaskList app icon.",
        ["这一块硬骨头，被你啃下来了。"] = "You tackled a tough one.",
        ["郑重记上一笔：今天很了不起。"] = "Make a note: you did great today.",
        ["重量级完成，值得为自己鼓掌。"] = "A major achievement. Give yourself a hand.",
        ["又跨过了一座山，继续向前。"] = "Another mountain crossed. Keep going.",
        ["专注了这么久，这一刻属于你。"] = "After all that focus, this moment is yours.",
        ["这一步，很扎实"] = "A solid step forward",
        ["节奏保持得不错"] = "You've found a good rhythm",
        ["又拿下了一块"] = "Another piece accomplished",
        ["继续，状态正好"] = "Keep going, you're on a roll",
        ["按天回看完成的任务，随时把前一天整理成可复制的日报"] = "Review completed tasks by day and turn the previous day into a report you can copy",
        ["上一天"] = "Previous day",
        ["下一天"] = "Next day",
        ["回到最近"] = "Latest",
        ["已完成的任务"] = "Completed tasks",
        ["生成前一天总结"] = "Report for previous day",
        ["可复制的日报文本"] = "Daily report · Ready to copy",
        ["总结日期往前一天"] = "Previous report day",
        ["总结日期往后一天"] = "Next report day",
        ["收起总结"] = "Close report",
        ["复制全部"] = "Copy all",
        ["恢复待办"] = "Reopen",
        ["删除"] = "Delete",
        ["这一天还没有完成记录，换个日期看看吧"] = "No completed tasks on this day. Try another date",
        ["勾选左侧圆圈即可完成，越重的任务仪式越足"] = "Check the circle to complete a task. Heavier tasks get bigger celebrations",
        ["完成 · 轻型任务"] = "Done · Light task",
        ["完成 · 重型任务"] = "Done · Heavy task",
        ["做得好，为你鼓掌"] = "Well done. A round of applause",
        ["这一程，很扎实"] = "You've made solid progress",
        ["为你热烈鼓掌"] = "A big round of applause",
        ["这一小步，也值得肯定"] = "This small step deserves recognition",
        ["认真推进的每一步，都算数"] = "Every thoughtful step forward counts",
        ["漂亮收官，掌声属于努力的你"] = "A great finish. Your hard work deserves applause",
        ["完成特效的动画目标不支持属性动画。"] = "The celebration target does not support property animations.",
        ["鼓掌轮廓必须使用三次贝塞尔线段。"] = "Applause contours must use cubic Bezier segments."
    };
}
