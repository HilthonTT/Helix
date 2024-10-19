namespace Helix.App.Behaviors;

public sealed partial class CursorBehavior : PlatformBehavior<View>
{
    public static readonly BindableProperty AttachBehaviorProperty =
       BindableProperty.CreateAttached(
           "AttachBehavior",
           typeof(bool),
           typeof(CursorBehavior),
           false,
           propertyChanged: OnAttachBehaviorChanged);

    public static bool GetAttachBehavior(BindableObject view)
    {
        return (bool)view.GetValue(AttachBehaviorProperty);
    }

    private static void OnAttachBehaviorChanged(BindableObject view, object oldValue, object newValue)
    {
        if (view is not Button button)
        {
            return;
        }

        bool attackBehavior = (bool)newValue;
        if (attackBehavior)
        {
            button.Behaviors.Add(new CursorBehavior());

            return;
        }

        Behavior? behaviorToRemove = button.Behaviors.FirstOrDefault(b => b is CursorBehavior);
        if (behaviorToRemove is not null)
        {
            button.Behaviors.Remove(behaviorToRemove);
        }
    }
}
