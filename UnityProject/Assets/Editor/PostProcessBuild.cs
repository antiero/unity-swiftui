using UnityEditor;
using UnityEditor.iOS.Xcode;
using System.Collections.Generic;

/* We could implement UnityEditor.Build.IPostprocessBuildWithReport interface
   but documentation is unclear if this is preferred over the below callback attribute.
   There is a known bug with the interface's BuildReport having wrong information, so we opt to use the attribute. */
public class PostProcessBuild
{
    [UnityEditor.Callbacks.PostProcessBuildAttribute]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        /* Edit Unity generated Xcode project to enable Unity as a library:
           github.com/Unity-Technologies/uaal-example/blob/master/docs/ios.md */
        if (target == BuildTarget.iOS)
        {
            // Read project
            string projectPath = PBXProject.GetPBXProjectPath(buildPath);
            PBXProject project = new PBXProject();
            project.ReadFromFile(projectPath);

            // Get main and framework target guids
            string unityMainTargetGuid = project.GetUnityMainTargetGuid();
            string unityFrameworkTargetGuid = project.GetUnityFrameworkTargetGuid();

            // Set NativeState plugin header visibility to public
            string pluginHeaderGuid = project.FindFileGuidByProjectPath("Libraries/Plugins/iOS/NativeState.h");
            project.AddPublicHeaderToBuild(unityFrameworkTargetGuid, pluginHeaderGuid);

            // Change data directory target membership to framework only
            string dataDirectoryGuid = project.FindFileGuidByProjectPath("Data");
            project.RemoveFileFromBuild(unityMainTargetGuid, dataDirectoryGuid);
            project.AddFileToBuild(unityFrameworkTargetGuid, dataDirectoryGuid);

            /* Add custom modulemap for NativeState plugin
               interop with Swift and set corresponding build setting:
               developer.apple.com/documentation/xcode/build-settings-reference#Module-Map-File */
            string modulemapRelativePath = "UnityFramework/UnityFramework.modulemap";
            string modulemapAbsolutePath = $"{buildPath}/{modulemapRelativePath}";
            FileUtil.ReplaceFile("Assets/Plugins/iOS/UnityFramework.modulemap", modulemapAbsolutePath);
            project.AddFile(modulemapAbsolutePath, modulemapRelativePath);
            project.AddBuildProperty(unityFrameworkTargetGuid, "MODULEMAP_FILE", modulemapRelativePath);
            string embedApplePluginScript = @"#  Apple Unity Plug-in Sign & Embed libraries shell script
#  Copyright © 2024 Apple, Inc. All rights reserved.
#  This script is added to the generated Xcode project by the Apple.Core plug-in.
#  Please see AppleBuild.cs in the Apple.Core plug-in for more information.
dstFrameworkFolder=""$BUILT_PRODUCTS_DIR/$FRAMEWORKS_FOLDER_PATH""
dstBundleFolder=""$BUILT_PRODUCTS_DIR/$PLUGINS_FOLDER_PATH""
APPLE_PLUGIN_LIBRARY_ROOT=""$PROJECT_DIR/ApplePluginLibraries""
if [ -d $APPLE_PLUGIN_LIBRARY_ROOT ]; then
    for folder in ""$APPLE_PLUGIN_LIBRARY_ROOT""/*; do
        if [ -d ""$folder"" ]; then
            for item in ""$folder""/*; do
                if [[ $item = *'.dSYM' ]]; then
                    continue
                elif [[ $item = *'.framework' ]]; then
                    filename=$(basename $item)
                    echo ""    Embedding Apple plug-in framework $filename""
                    echo ""      Source: $item""
                    echo ""      Destination: $dstFrameworkFolder/$filename""
                    if [ ! -z ""$EXPANDED_CODE_SIGN_IDENTITY"" ]; then
                        echo ""      Code signing identity: $EXPANDED_CODE_SIGN_IDENTITY""
                        codesign --force --sign $EXPANDED_CODE_SIGN_IDENTITY --timestamp\=none --generate-entitlement-der $item
                    fi
                    ditto $item ""$dstFrameworkFolder/$filename""
                    break
                elif [[ $item = *'.bundle' ]]; then
                    filename=$(basename $item)
                    echo ""    Embedding Apple plug-in bundle $filename""
                    echo ""      Source: $item""
                    echo ""      Destination: $dstBundleFolder/$filename""
                    if [ ! -z ""$EXPANDED_CODE_SIGN_IDENTITY"" ]; then
                        echo ""      Code signing identity: $EXPANDED_CODE_SIGN_IDENTITY""
                        codesign --force --sign $EXPANDED_CODE_SIGN_IDENTITY --timestamp\=none --generate-entitlement-der $item
                    fi
                    ditto $item ""$dstBundleFolder/$filename""
                    break
                fi
            done
        fi
    done
    echo ""Completed search of libraries in the Apple native plug-in folder root.""
else
    echo ""No Apple plug-in library path found at $APPLE_PLUGIN_LIBRARY_ROOT""
    echo ""Please build a Development Build in Unity for this script to log more information.""
    exit 1
fi
";

            List<string> inputPaths = new();
            project.InsertShellScriptBuildPhase(7, unityFrameworkTargetGuid,
                "Embed Apple Plug-in Libraries",
                "/bin/sh",
                embedApplePluginScript,
                inputPaths);

            // Overwrite project
            project.WriteToFile(projectPath);
        }
    }
}
