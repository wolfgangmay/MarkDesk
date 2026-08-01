using System.Runtime.CompilerServices;
using System.Text;

namespace MarkDesk.Tests;

public static class TestModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
