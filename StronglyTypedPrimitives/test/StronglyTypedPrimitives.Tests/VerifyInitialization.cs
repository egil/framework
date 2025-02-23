using System.Runtime.CompilerServices;
using DiffEngine;

namespace StronglyTypedPrimitives;

public static class VerifyInitialization
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.InitializePlugins();
        DiffTools.UseOrder(DiffTool.VisualStudioCode);
        VerifierSettings.AutoVerify();
    }
}