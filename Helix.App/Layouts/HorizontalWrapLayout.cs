using Microsoft.Maui.Layouts;

namespace Helix.App.Layouts;

internal sealed class HorizontalWrapLayout : StackLayout
{
    protected override ILayoutManager CreateLayoutManager()
    {
        return new HorizontalWrapLayoutManager(this);
    }
}
