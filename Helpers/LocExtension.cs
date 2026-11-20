using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using TaskList.Services;

namespace TaskList.Helpers;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding(nameof(LocalizedText.Value))
        {
            Source = new LocalizedText(Key),
            Mode = BindingMode.OneWay
        }.ProvideValue(serviceProvider);

    private sealed class LocalizedText : INotifyPropertyChanged
    {
        private readonly string _key;

        public LocalizedText(string key)
        {
            _key = key;
            PropertyChangedEventManager.AddHandler(LocalizationService.Instance, OnLanguageChanged, string.Empty);
        }

        public string Value => LocalizationService.T(_key);
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}
