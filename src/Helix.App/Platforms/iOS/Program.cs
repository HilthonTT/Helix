using ObjCRuntime;
using UIKit;

namespace Helix.App;

public sealed class Program
{
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
