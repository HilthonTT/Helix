using Microsoft.Maui.Layouts;

namespace Helix.App.Controls;

internal sealed class HorizontalWrapLayout : StackLayout
{
    protected override ILayoutManager CreateLayoutManager()
    {
        return new HorizontalWrapLayoutManager(this);
    }
}
