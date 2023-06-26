#addin nuget:?package=Cake.Xamarin&version=3.1.0
#addin nuget:?package=Cake.XCode&version=5.0.0
#addin nuget:?package=Cake.FileHelpers&version=4.0.1
#addin nuget:?package=Cake.Json&version=6.0.1
#addin nuget:?package=NuGet.Packaging.Core&version=5.11.0
#addin nuget:?package=SharpCompress&version=0.32.2
#addin nuget:?package=Mono.Cecil&version=0.10.0
#addin nuget:?package=Mono.ApiTools&version=5.14.0.2
#addin nuget:?package=Mono.ApiTools.NuGetDiff&version=1.3.2
#addin nuget:?package=Xamarin.Nuget.Validator&version=1.1.1

#tool nuget:?package=mdoc&version=5.8.9
#tool nuget:?package=xunit.runner.console&version=2.4.2
#tool nuget:?package=vswhere&version=2.8.4

using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SharpCompress.Common;
using SharpCompress.Readers;
using Mono.ApiTools;
using NuGet.Packaging;
using NuGet.Versioning;

DirectoryPath ROOT_PATH = MakeAbsolute(Directory("."));

#load "../scripts/cake/shared.cake"
#load "../scripts/cake/native-shared.cake"

var SKIP_EXTERNALS = Argument ("skipexternals", "")
    .ToLower ().Split (new [] { ',' }, StringSplitOptions.RemoveEmptyEntries);
var SKIP_BUILD = Argument ("skipbuild", false);
var PACK_ALL_PLATFORMS = Argument ("packall", Argument ("PackAllPlatforms", false));
var PRINT_ALL_ENV_VARS = Argument ("printAllEnvVars", false);
var UNSUPPORTED_TESTS = Argument ("unsupportedTests", "");
var THROW_ON_TEST_FAILURE = Argument ("throwOnTestFailure", true);
var NUGET_DIFF_PRERELEASE = Argument ("nugetDiffPrerelease", false);
var COVERAGE = Argument ("coverage", false);
var CHROMEWEBDRIVER = Argument ("chromedriver", EnvironmentVariable ("CHROMEWEBDRIVER"));

var PLATFORM_SUPPORTS_VULKAN_TESTS = (IsRunningOnWindows () || IsRunningOnLinux ()).ToString ();
var SUPPORT_VULKAN_VAR = Argument ("supportVulkan", EnvironmentVariable ("SUPPORT_VULKAN") ?? PLATFORM_SUPPORTS_VULKAN_TESTS);
var SUPPORT_VULKAN = SUPPORT_VULKAN_VAR == "1" || SUPPORT_VULKAN_VAR.ToLower () == "true";

var MDocPath = Context.Tools.Resolve ("mdoc.exe");

DirectoryPath DOCS_ROOT_PATH = ROOT_PATH.Combine("docs");
DirectoryPath DOCS_PATH = DOCS_ROOT_PATH.Combine("SkiaSharpAPI");

var PREVIEW_LABEL = Argument ("previewLabel", EnvironmentVariable ("PREVIEW_LABEL") ?? "preview");
var FEATURE_NAME = EnvironmentVariable ("FEATURE_NAME") ?? "";
var BUILD_NUMBER = Argument ("buildNumber", EnvironmentVariable ("BUILD_NUMBER") ?? "0");
var BUILD_COUNTER = Argument ("buildCounter", EnvironmentVariable ("BUILD_COUNTER") ?? BUILD_NUMBER);
var GIT_SHA = Argument ("gitSha", EnvironmentVariable ("GIT_SHA") ?? "");
var GIT_BRANCH_NAME = Argument ("gitBranch", EnvironmentVariable ("GIT_BRANCH_NAME") ?? ""). Replace ("refs/heads/", "");
var GIT_URL = Argument ("gitUrl", EnvironmentVariable ("GIT_URL") ?? "");

var PREVIEW_NUGET_SUFFIX = "";
if (!string.IsNullOrEmpty (FEATURE_NAME)) {
    PREVIEW_NUGET_SUFFIX = $"featurepreview-{FEATURE_NAME}";
} else {
    PREVIEW_NUGET_SUFFIX = $"{PREVIEW_LABEL}";
}
if (!string.IsNullOrEmpty (BUILD_NUMBER)) {
    PREVIEW_NUGET_SUFFIX += $".{BUILD_NUMBER}";
}

var CURRENT_PLATFORM = "";
if (IsRunningOnWindows ()) {
    CURRENT_PLATFORM = "Windows";
} else if (IsRunningOnMacOs ()) {
    CURRENT_PLATFORM = "Mac";
} else if (IsRunningOnLinux ()) {
    CURRENT_PLATFORM = "Linux";
} else {
    throw new Exception ("This script is not running on a known platform.");
}

var PREVIEW_FEED_URL = Argument ("previewFeed", "https://pkgs.dev.azure.com/xamarin/public/_packaging/SkiaSharp/nuget/v3/index.json");

var TRACKED_NUGETS = new Dictionary<string, Version> {
    { "SkiaSharp",                                     new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.Linux",                  new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.Linux.NoDependencies",   new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.NanoServer",             new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.WebAssembly",            new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.Android",                new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.iOS",                    new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.MacCatalyst",            new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.macOS",                  new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.Tizen",                  new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.tvOS",                   new Version (1, 60, 0) },
    { "SkiaSharp.NativeAssets.Win32",                  new Version (1, 60, 0) },
    { "SkiaSharp.Views",                               new Version (1, 60, 0) },
    { "SkiaSharp.Views.Desktop.Common",                new Version (1, 60, 0) },
    { "SkiaSharp.Views.Gtk2",                          new Version (1, 60, 0) },
    { "SkiaSharp.Views.Gtk3",                          new Version (1, 60, 0) },
    { "SkiaSharp.Views.WindowsForms",                  new Version (1, 60, 0) },
    { "SkiaSharp.Views.WPF",                           new Version (1, 60, 0) },
    { "SkiaSharp.Views.Uno",                           new Version (1, 60, 0) },
    { "SkiaSharp.Views.Uno.WinUI",                     new Version (1, 60, 0) },
    { "SkiaSharp.Views.WinUI",                         new Version (1, 60, 0) },
    { "SkiaSharp.Views.Maui.Core",                     new Version (1, 60, 0) },
    { "SkiaSharp.Views.Maui.Controls",                 new Version (1, 60, 0) },
    { "SkiaSharp.Views.Maui.Controls.Compatibility",   new Version (1, 60, 0) },
    { "SkiaSharp.Views.Blazor",                        new Version (1, 60, 0) },
    { "HarfBuzzSharp",                                 new Version (1, 0, 0) },
    { "HarfBuzzSharp.NativeAssets.Android",            new Version (1, 0, 0) },
    { "HarfBuzzSharp.NativeAssets.iOS",                new Version (1, 0, 0) },
    { "HarfBuzzSharp.NativeAssets.Linux",              new Version (1, 0, 0) },
    { "HarfBuzzSharp.NativeAssets.MacCatalyst",        new Version (1, 0, 0) },
    { "HarfBuzzSharp.NativeAssets.macOS",              new Version (1, 0, 0) },
    { "HarfBuzzSharp.NativeAssets.Tizen",              new Version (1, 0, 0) },
    { "HarfBuzzSharp.NativeAssets.tvOS",               new Version (1, 0, 0) },
    { "HarfBuzzSharp.NativeAssets.WebAssembly",        new Version (1, 0, 0) },
    { "HarfBuzzSharp.NativeAssets.Win32",              new Version (1, 0, 0) },
    { "SkiaSharp.HarfBuzz",                            new Version (1, 60, 0) },
    { "SkiaSharp.Skottie",                             new Version (1, 60, 0) },
    { "SkiaSharp.SceneGraph",                          new Version (1, 60, 0) },
    { "SkiaSharp.Vulkan.SharpVk",                      new Version (1, 60, 0) },
};

var PREVIEW_ONLY_NUGETS = new List<string> {
};

Information("Source Control:");
Information($"    {"PREVIEW_LABEL".PadRight(30)} {{0}}", PREVIEW_LABEL);
Information($"    {"FEATURE_NAME".PadRight(30)} {{0}}", FEATURE_NAME);
Information($"    {"BUILD_NUMBER".PadRight(30)} {{0}}", BUILD_NUMBER);
Information($"    {"GIT_SHA".PadRight(30)} {{0}}", GIT_SHA);
Information($"    {"GIT_BRANCH_NAME".PadRight(30)} {{0}}", GIT_BRANCH_NAME);
Information($"    {"GIT_URL".PadRight(30)} {{0}}", GIT_URL);

#load "../scripts/cake/msbuild.cake"
#load "../scripts/cake/UtilsManaged.cake"
#load "../scripts/cake/externals.cake"
#load "../scripts/cake/UpdateDocs.cake"
#load "../scripts/cake/samples.cake"

Task ("__________________________________")
    .Description ("__________________________________________________");

////////////////////////////////////////////////////////////////////////////////////////////////////
// EXTERNALS - the native C and C++ libraries
////////////////////////////////////////////////////////////////////////////////////////////////////

// this builds all the externals
Task ("externals")
    .Description ("Build all external dependencies.")
    .IsDependentOn ("externals-native");

////////////////////////////////////////////////////////////////////////////////////////////////////
// LIBS - the managed C# libraries
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("libs")
    .Description ("Build all managed assemblies.")
    .WithCriteria(!SKIP_BUILD)
    .IsDependentOn ("externals")
    .Does (() =>
{
    // build the managed libraries
    RunDotNetBuild ($"./source/SkiaSharpSource.{CURRENT_PLATFORM}.slnf");

    // assemble the mdoc docs
    EnsureDirectoryExists ("./output/docs/mdoc/");
    RunProcess (MDocPath, new ProcessSettings {
        Arguments = $"assemble --out=\"./output/docs/mdoc/SkiaSharp\" \"{DOCS_PATH}\" --debug",
    });
    CopyFileToDirectory ("./docs/SkiaSharp.source", "./output/docs/mdoc/");
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// TESTS - some test cases to make sure it works
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("tests")
    .Description ("Run all tests.")
    .IsDependentOn ("tests-netfx")
    .IsDependentOn ("tests-netcore")
    .IsDependentOn ("tests-android")
    .IsDependentOn ("tests-ios");

Task ("tests-netfx")
    .Description ("Run all Full .NET Framework tests.")
    .IsDependentOn ("externals")
    .Does (() =>
{
    var failedTests = 0;

    void RunDesktopTest (string arch)
    {
        if (Skip(arch)) return;

        RunMSBuild ("./tests/SkiaSharp.Desktop.Tests.sln", platform: arch == "AnyCPU" ? "Any CPU" : arch);

        // SkiaSharp.Tests.dll
        try {
            RunTests ($"./tests/SkiaSharp.Desktop.Tests/bin/{arch}/{CONFIGURATION}/SkiaSharp.Tests.dll", arch == "x86");
        } catch {
            failedTests++;
        }

        // SkiaSharp.Vulkan.Tests.dll
        if (SUPPORT_VULKAN) {
            try {
                RunTests ($"./tests/SkiaSharp.Vulkan.Desktop.Tests/bin/{arch}/{CONFIGURATION}/SkiaSharp.Vulkan.Tests.dll", arch == "x86");
            } catch {
                failedTests++;
            }
        }
    }

    CleanDirectories ($"{PACKAGE_CACHE_PATH}/skiasharp*");
    CleanDirectories ($"{PACKAGE_CACHE_PATH}/harfbuzzsharp*");

    if (IsRunningOnWindows ()) {
        RunDesktopTest ("x86");
        RunDesktopTest ("x64");
    } else if (IsRunningOnMacOs ()) {
        RunDesktopTest ("AnyCPU");
    } else if (IsRunningOnLinux ()) {
        RunDesktopTest ("x64");
    }

    if (failedTests > 0) {
        if (THROW_ON_TEST_FAILURE)
            throw new Exception ($"There were {failedTests} failed tests.");
        else
            Warning ($"There were {failedTests} failed tests.");
    }
});

Task ("tests-netcore")
    .Description ("Run all .NET Core tests.")
    .IsDependentOn ("externals")
    .Does (() =>
{
    var failedTests = 0;

    CleanDirectories ($"{PACKAGE_CACHE_PATH}/skiasharp*");
    CleanDirectories ($"{PACKAGE_CACHE_PATH}/harfbuzzsharp*");

    // SkiaSharp.NetCore.Tests.csproj
    try {
        RunNetCoreTests ("./tests/SkiaSharp.NetCore.Tests/SkiaSharp.NetCore.Tests.csproj");
    } catch {
        failedTests++;
    }

    // SkiaSharp.Vulkan.NetCore.Tests.csproj
    if (SUPPORT_VULKAN) {
        try {
            RunNetCoreTests ("./tests/SkiaSharp.Vulkan.NetCore.Tests/SkiaSharp.Vulkan.NetCore.Tests.csproj");
        } catch {
            failedTests++;
        }
    }

    if (failedTests > 0) {
        if (THROW_ON_TEST_FAILURE)
            throw new Exception ($"There were {failedTests} failed tests.");
        else
            Warning ($"There were {failedTests} failed tests.");
    }
    if (COVERAGE) {
        RunCodeCoverage ("./tests/**/Coverage/**/*.xml", "./output/coverage");
    }
});

Task ("tests-android")
    .Description ("Run all Android tests.")
    .IsDependentOn ("externals")
    .Does (() =>
{
    var failedTests = 0;

    CleanDirectories ($"{PACKAGE_CACHE_PATH}/skiasharp*");
    CleanDirectories ($"{PACKAGE_CACHE_PATH}/harfbuzzsharp*");

    // SkiaSharp.Android.Tests.csproj
    try {
        // build the solution to copy all the files
        RunMSBuild ("./tests/SkiaSharp.Android.Tests.sln", configuration: "Debug");
        // package the app
        FilePath csproj = "./tests/SkiaSharp.Android.Tests/SkiaSharp.Android.Tests.csproj";
        RunMSBuild (csproj,
            targets: new [] { "SignAndroidPackage" },
            properties: new Dictionary<string, string> {
                { "BuildTestOnly", "true" },
            },
            platform: "AnyCPU",
            configuration: "Debug");
        // run the tests
        DirectoryPath results = "./output/logs/testlogs/SkiaSharp.Android.Tests";
        RunCake ("./../scripts/cake/xharness-android.cake", "Default", new Dictionary<string, string> {
            { "project", MakeAbsolute(csproj).FullPath },
            { "configuration", "Debug" },
            { "exclusive", "true" },
            { "results", MakeAbsolute(results).FullPath },
            { "verbosity", "diagnostic" },
        });
    } catch {
        failedTests++;
    }

    if (failedTests > 0) {
        if (THROW_ON_TEST_FAILURE)
            throw new Exception ($"There were {failedTests} failed tests.");
        else
            Warning ($"There were {failedTests} failed tests.");
    }
});

Task ("tests-ios")
    .Description ("Run all iOS tests.")
    .IsDependentOn ("externals")
    .Does (() =>
{
    var failedTests = 0;

    CleanDirectories ($"{PACKAGE_CACHE_PATH}/skiasharp*");
    CleanDirectories ($"{PACKAGE_CACHE_PATH}/harfbuzzsharp*");

    // SkiaSharp.iOS.Tests.csproj
    try {
        // build the solution to copy all the files
        RunMSBuild ("./tests/SkiaSharp.iOS.Tests.sln", configuration: "Debug");
        // package the app
        FilePath csproj = "./tests/SkiaSharp.iOS.Tests/SkiaSharp.iOS.Tests.csproj";
        RunMSBuild (csproj,
            properties: new Dictionary<string, string> {
                { "BuildIpa", "true" },
                { "BuildTestOnly", "true" },
            },
            platform: "iPhoneSimulator",
            configuration: "Debug");
        // run the tests
        DirectoryPath results = "./output/logs/testlogs/SkiaSharp.iOS.Tests";
        RunCake ("./../scripts/cake/xharness-ios.cake", "Default", new Dictionary<string, string> {
            { "project", MakeAbsolute(csproj).FullPath },
            { "configuration", "Debug" },
            { "exclusive", "true" },
            { "results", MakeAbsolute(results).FullPath },
        });
    } catch {
        failedTests++;
    }

    if (failedTests > 0) {
        if (THROW_ON_TEST_FAILURE)
            throw new Exception ($"There were {failedTests} failed tests.");
        else
            Warning ($"There were {failedTests} failed tests.");
    }
});

Task ("tests-wasm")
    .Description ("Run WASM tests.")
    .IsDependentOn ("externals-wasm")
    .Does (() =>
{
    var failedTests = 0;

    RunMSBuild ("./tests/SkiaSharp.Wasm.Tests.sln");

    var pubDir = "./tests/SkiaSharp.Wasm.Tests/bin/publish/";
    RunNetCorePublish("./tests/SkiaSharp.Wasm.Tests/SkiaSharp.Wasm.Tests.csproj", pubDir);
    IProcess serverProc = null;
    try {
        serverProc = RunAndReturnProcess(PYTHON_EXE, new ProcessSettings {
            Arguments = "server.py",
            WorkingDirectory = pubDir,
        });
        DotNetCoreRun("./utils/WasmTestRunner/WasmTestRunner.csproj",
            "--output=\"./tests/SkiaSharp.Wasm.Tests/TestResults/\" " +
            (string.IsNullOrEmpty(CHROMEWEBDRIVER) ? "" : $"--driver=\"{CHROMEWEBDRIVER}\" ") +
            "--verbose " +
            "\"http://127.0.0.1:8000/\" ");
    } catch {
        failedTests++;
    } finally {
        serverProc?.Kill();
    }

    if (failedTests > 0) {
        if (THROW_ON_TEST_FAILURE)
            throw new Exception ($"There were {failedTests} failed tests.");
        else
            Warning ($"There were {failedTests} failed tests.");
    }
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// SAMPLES - the demo apps showing off the work
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("samples-generate")
    .Description ("Generate and zip the samples directory structure.")
    .Does (() =>
{
    EnsureDirectoryExists ("./output/");

    // create the interactive archive
    Zip ("./interactive", "./output/interactive.zip");

    // create the samples archive
    CreateSamplesDirectory ("./samples/", "./output/samples/");
    Zip ("./output/samples/", "./output/samples.zip");

    // create the preview samples archive
    CreateSamplesDirectory ("./samples/", "./output/samples-preview/", PREVIEW_NUGET_SUFFIX);
    Zip ("./output/samples-preview/", "./output/samples-preview.zip");
});

Task ("samples")
    .Description ("Build all samples.")
    .IsDependentOn ("samples-generate")
    .Does(() =>
{
    var isLinux = IsRunningOnLinux ();
    var isMac = IsRunningOnMacOs ();
    var isWin = IsRunningOnWindows ();

    var buildMatrix = new Dictionary<string, bool> {
        { "android", isMac || isWin },
        { "gtk", isLinux || isMac },
        { "ios", isMac },
        { "macos", isMac },
        { "tvos", isMac },
        { "winui", isWin },
        { "wapproj", isWin },
        { "msix", isWin },
        { "wpf", isWin },
    };

    var platformMatrix = new Dictionary<string, string> {
        { "ios", "iPhone" },
        { "tvos", "iPhoneSimulator" },
        { "winui", "x64" },
        { "wapproj", "x64" },
        { "xamarin.forms.mac", "iPhone" },
        { "xamarin.forms.windows", "x86" },
    };

    void BuildSample (FilePath sln, bool useNetCore, bool dryrun)
    {
        var platform = sln.GetDirectory ().GetDirectoryName ().ToLower ();
        var name = sln.GetFilenameWithoutExtension ();
        var slnPlatform = name.GetExtension ();
        if (!string.IsNullOrEmpty (slnPlatform)) {
            slnPlatform = slnPlatform.ToLower ();
        }

        if (!buildMatrix.ContainsKey (platform) || buildMatrix [platform]) {
            string buildPlatform = null;
            if (!string.IsNullOrEmpty (slnPlatform)) {
                if (platformMatrix.ContainsKey (platform + slnPlatform)) {
                    buildPlatform = platformMatrix [platform + slnPlatform];
                }
            }
            if (string.IsNullOrEmpty (buildPlatform) && platformMatrix.ContainsKey (platform)) {
                buildPlatform = platformMatrix [platform];
            }

            if (dryrun) {
                if (useNetCore)
                    Information ($"    BUILD  (DN) {sln}");
                else
                    Information ($"    BUILD  (FX) {sln}");
            } else {
                Information ($"Building sample {sln} ({platform})...");
                if (useNetCore) {
                    RunDotNetBuild (sln);
                } else {
                    RunNuGetRestorePackagesConfig (sln);
                    RunMSBuild (sln, platform: buildPlatform);
                }
            }
        } else {
            if (dryrun) {
                Information ($"    SKIP   (NS) {sln} (not supported)");
            } else {
                Information ($"Skipping sample {sln} ({platform})...");
            }
        }
    }

    // build the newly migrated samples
    CleanDirectories ($"{PACKAGE_CACHE_PATH}/skiasharp*");
    CleanDirectories ($"{PACKAGE_CACHE_PATH}/harfbuzzsharp*");

    // TODO: Docker seems to be having issues on the old DevOps agents

    // // copy all the packages next to the Dockerfile files
    // var dockerfiles = GetFiles ("./output/samples/**/Dockerfile");
    // foreach (var dockerfile in dockerfiles) {
    //     CopyDirectory (OUTPUT_NUGETS_PATH, dockerfile.GetDirectory ().Combine ("packages"));
    // }

    // // build the run.ps1 (typically for Dockerfiles)
    // var runs = GetFiles ("./output/samples/**/run.ps1");
    // foreach (var run in runs) {
    //     RunProcess ("pwsh", new ProcessSettings {
    //         Arguments = run.FullPath,
    //         WorkingDirectory = run.GetDirectory (),
    //     });
    // }

    // build solutions locally
    var actualSamples = PREVIEW_ONLY_NUGETS.Count > 0
        ? "samples-preview"
        : "samples";
    var solutions =
        GetFiles ($"./output/{actualSamples}/**/*.sln").Union (
        GetFiles ($"./output/{actualSamples}/**/*.slnf"))
        .OrderBy (x => x.FullPath)
        .ToArray ();

    Information ("Solutions found:");
    foreach (var sln in solutions) {
        Information ("    " + sln);
    }

    foreach (var dryrun in new [] { true, false }) {
        if (dryrun)
            Information ("Sample builds:");

        foreach (var sln in solutions) {
            // might have been deleted due to a platform build and cleanup
            if (!FileExists (sln))
                continue;

            var name = sln.GetFilenameWithoutExtension ();

            // this is a IDE only solution
            if (name.ToString ().EndsWith ("-vsmac"))
                continue;

            var slnPlatform = name.GetExtension ();

            if (string.IsNullOrEmpty (slnPlatform)) {
                // this is the main solution
                var variants =
                    GetFiles (sln.GetDirectory ().CombineWithFilePath (name) + ".*.sln").Union (
                    GetFiles (sln.GetDirectory ().CombineWithFilePath (name) + ".*.slnf"));
                if (!variants.Any ()) {
                    // there is no platform variant
                    BuildSample (sln, false, dryrun);
                    // delete the built sample
                    if (!dryrun)
                        CleanDir (sln.GetDirectory ().FullPath);
                } else {
                    // skip as there is a platform variant
                    if (dryrun)
                        Information ($"    SKIP   (PS) {sln} (has platform specific)");
                }
            } else {
                // this is a platform variant
                slnPlatform = slnPlatform.ToLower ();
                var useNetCore = slnPlatform.EndsWith ("-net");
                if (useNetCore)
                    slnPlatform = slnPlatform.Substring (0, slnPlatform.Length - 4);

                var shouldBuild =
                    (isLinux && slnPlatform == ".linux") ||
                    (isMac && slnPlatform == ".mac") ||
                    (isWin && slnPlatform == ".windows");
                if (shouldBuild) {
                    BuildSample (sln, useNetCore, dryrun);
                    // delete the built sample
                    if (!dryrun) {
                        CleanDir (sln.GetDirectory ().FullPath);
                    }
                } else {
                    // skip this as this is not the correct platform
                    if (dryrun)
                        Information ($"    SKIP   (AP) {sln} (has alternate platform)");
                }
            }
        }
    }

    CleanDir ("./output/samples/");
    DeleteDir ("./output/samples/");
    CleanDir ("./output/samples-preview/");
    DeleteDir ("./output/samples-preview/");
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// NUGET - building the package for NuGet.org
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("nuget")
    .Description ("Pack all NuGets.")
    .IsDependentOn ("nuget-normal")
    .IsDependentOn ("nuget-special");

Task ("nuget-normal")
    .Description ("Pack all NuGets (build all required dependencies).")
    .IsDependentOn ("libs")
    .Does (() =>
{
    // pack the packages (stable and then preview versions)
    RunDotNetPack ($"./source/SkiaSharpSource.{CURRENT_PLATFORM}.slnf");
    RunDotNetPack ($"./source/SkiaSharpSource.{CURRENT_PLATFORM}.slnf", versionSuffix: PREVIEW_NUGET_SUFFIX);

    // move symbols to a special location to avoid signing
    EnsureDirectoryExists ($"{OUTPUT_SYMBOLS_NUGETS_PATH}");
    DeleteFiles ($"{OUTPUT_SYMBOLS_NUGETS_PATH}/*.nupkg");
    MoveFiles ($"{OUTPUT_NUGETS_PATH}/*.snupkg", OUTPUT_SYMBOLS_NUGETS_PATH);
    MoveFiles ($"{OUTPUT_NUGETS_PATH}/*.symbols.nupkg", OUTPUT_SYMBOLS_NUGETS_PATH);

    // setup validation options
    var options = new Xamarin.Nuget.Validator.NugetValidatorOptions {
        Copyright = "© Microsoft Corporation. All rights reserved.",
        Author = "Microsoft",
        Owner = "Microsoft",
        NeedsProjectUrl = true,
        NeedsLicenseUrl = true,
        ValidateRequireLicenseAcceptance = true,
        ValidPackageNamespace = new [] { "SkiaSharp", "HarfBuzzSharp" },
    };

    var nupkgFiles = GetFiles ($"{OUTPUT_NUGETS_PATH}/*.nupkg");

    Information ("Found ({0}) Nuget's to validate", nupkgFiles.Count ());

    foreach (var nupkgFile in nupkgFiles) {
        Verbose ("Verifiying Metadata of {0}", nupkgFile.GetFilename ());

        var result = Xamarin.Nuget.Validator.NugetValidator.Validate(MakeAbsolute(nupkgFile).FullPath, options);
        if (!result.Success) {
            Information ("Metadata validation failed for: {0} \n\n", nupkgFile.GetFilename ());
            Information (string.Join("\n    ", result.ErrorMessages));
            throw new Exception ($"Invalid Metadata for: {nupkgFile.GetFilename ()}");

        } else {
            Information ("Metadata validation passed for: {0}", nupkgFile.GetFilename ());
        }
    }
});

Task ("nuget-special")
    .Description ("Pack all special NuGets.")
    .IsDependentOn ("nuget-normal")
    .Does (() =>
{
    EnsureDirectoryExists ($"{OUTPUT_SPECIAL_NUGETS_PATH}");
    DeleteFiles ($"{OUTPUT_SPECIAL_NUGETS_PATH}/*.nupkg");

    // get a list of all the version number variants
    var versions = new List<string> ();
    if (!string.IsNullOrEmpty (PREVIEW_LABEL) && PREVIEW_LABEL.StartsWith ("pr.")) {
        var v = $"0.0.0-{PREVIEW_LABEL}";
        if (!string.IsNullOrEmpty (BUILD_COUNTER))
            v += $".{BUILD_COUNTER}";
        versions.Add (v);
    } else {
        if (!string.IsNullOrEmpty (GIT_SHA)) {
            var v = $"0.0.0-commit.{GIT_SHA}";
            if (!string.IsNullOrEmpty (BUILD_COUNTER))
                v += $".{BUILD_COUNTER}";
            versions.Add (v);
        }
        if (!string.IsNullOrEmpty (GIT_BRANCH_NAME)) {
            var v = $"0.0.0-branch.{GIT_BRANCH_NAME.Replace ("/", ".")}";
            if (!string.IsNullOrEmpty (BUILD_COUNTER))
                v += $".{BUILD_COUNTER}";
            versions.Add (v);
        }
    }

    // get a list of all the nuspecs to pack
    var specials = new Dictionary<string, string> ();

    var nativePlatforms = GetDirectories ("./output/native/*")
        .Select (d => d.GetDirectoryName ())
        .ToArray ();
    if (nativePlatforms.Length > 0) {
        specials[$"_NativeAssets"] = $"native";
        foreach (var platform in nativePlatforms) {
            specials[$"_NativeAssets.{platform}"] = $"native/{platform}";
        }
    }
    if (GetFiles ("./output/nugets/*.nupkg").Count > 0) {
        specials[$"_NuGets"] = $"nugets";
        specials[$"_NuGetsPreview"] = $"nugets";
        specials[$"_Symbols"] = $"nugets-symbols";
        specials[$"_SymbolsPreview"] = $"nugets-symbols";
    }

    foreach (var pair in specials) {
        var id = pair.Key;
        var path = pair.Value;
        var nuspec = $"./output/{path}/{id}.nuspec";

        DeleteFiles ($"./output/{path}/*.nuspec");

        foreach (var packageVersion in versions) {
            // update the version
            var fn = id.StartsWith ("_NativeAssets.") ? "_NativeAssets" : id;
            var xdoc = XDocument.Load ($"./scripts/nuget/{fn}.nuspec");
            var metadata = xdoc.Root.Element ("metadata");
            metadata.Element ("version").Value = packageVersion;
            metadata.Element ("id").Value = id;

            if (id == "_NativeAssets") {
                // handle the root package
                var dependencies = metadata.Element ("dependencies");
                foreach (var platform in nativePlatforms) {
                    dependencies.Add (new XElement ("dependency",
                        new XAttribute ("id", $"_NativeAssets.{platform}"),
                        new XAttribute ("version", packageVersion)));
                }
            } else if (id.StartsWith ("_NativeAssets.")) {
                // handle the dependencies
                var platform = id.Substring (id.IndexOf (".") + 1);
                var files = xdoc.Root.Element ("files");
                files.Add (new XElement ("file",
                    new XAttribute ("src", $"**"),
                    new XAttribute ("target", $"tools/{platform}")));
            }

            xdoc.Save (nuspec);
            PackageNuGet (nuspec, OUTPUT_SPECIAL_NUGETS_PATH, true);
        }

        DeleteFiles ($"./output/{path}/*.nuspec");
    }
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// DOCS - creating the xml, markdown and other documentation
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("update-docs")
    .Description ("Regenerate all docs.")
    .IsDependentOn ("docs-api-diff")
    .IsDependentOn ("docs-update-frameworks")
    .IsDependentOn ("docs-format-docs");

////////////////////////////////////////////////////////////////////////////////////////////////////
// CLEAN - remove all the build artefacts
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("clean")
    .Description ("Clean up.")
    .IsDependentOn ("clean-externals")
    .IsDependentOn ("clean-managed");

Task ("clean-managed")
    .Description ("Clean up (managed only).")
    .Does (() =>
{
    CleanDirectories ("./binding/*/bin");
    CleanDirectories ("./binding/*/obj");
    DeleteFiles ("./binding/*/project.lock.json");

    CleanDirectories ("./samples/*/*/bin");
    CleanDirectories ("./samples/*/*/obj");
    CleanDirectories ("./samples/*/*/AppPackages");
    CleanDirectories ("./samples/*/*/*/bin");
    CleanDirectories ("./samples/*/*/*/obj");
    DeleteFiles ("./samples/*/*/*/project.lock.json");
    CleanDirectories ("./samples/*/*/*/AppPackages");
    CleanDirectories ("./samples/*/*/packages");

    CleanDirectories ("./tests/**/bin");
    CleanDirectories ("./tests/**/obj");
    CleanDirectories ("./tests/**/artifacts");
    DeleteFiles ("./tests/**/project.lock.json");

    CleanDirectories ("./source/*/*/bin");
    CleanDirectories ("./source/*/*/obj");
    DeleteFiles ("./source/*/*/project.lock.json");
    CleanDirectories ("./source/*/*/Generated Files");
    CleanDirectories ("./source/packages");

    DeleteDir ("./output");
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// DEFAULT - target for common development
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("----------------------------------")
    .Description ("--------------------------------------------------");

Task ("Default")
    .Description ("Build all managed assemblies and external dependencies.")
    .IsDependentOn ("externals")
    .IsDependentOn ("libs");

Task ("Everything")
    .Description ("Build, pack and test everything.")
    .IsDependentOn ("externals")
    .IsDependentOn ("libs")
    .IsDependentOn ("nuget")
    .IsDependentOn ("tests")
    .IsDependentOn ("samples");

////////////////////////////////////////////////////////////////////////////////////////////////////
// BUILD NOW
////////////////////////////////////////////////////////////////////////////////////////////////////

RunTarget (TARGET);
