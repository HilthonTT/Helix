namespace Helix.App;

[AcceptEmptyServiceProvider]
[ContentProperty(nameof(Name))]
internal sealed class TranslateExtension : IMarkupExtension<BindingBase>
{
    public string Name { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding()
        {
            Mode = BindingMode.OneWay,
            Path = $"[{Name}]",
            Source = LocalizationResourceManager.Instance,
        };
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
    {
        return ProvideValue(serviceProvider);
    }
}
