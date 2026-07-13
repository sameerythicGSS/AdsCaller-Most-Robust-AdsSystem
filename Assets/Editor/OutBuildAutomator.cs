#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Automates the build path selection, validates configuration, manages versioning, and enforces strict naming conventions.
/// Universally adaptable for any system or team member.
/// </summary>
public class OutBuildAutomator
{
    private const string PrefKey = "OutBuildAutomator_LastPath";

    // Dynamically grabs the directory just outside the "Assets" folder to keep builds relative to the project.
    private static string DefaultStartPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds");

    #region Menu Items

    [MenuItem("Build Automator/Build %&b", false, 1)] // Ctrl+Alt+B
    public static void BuildOnly()
    {
        ExecuteBuildSetup(BuildOptions.None);
    }

    [MenuItem("Build Automator/Build and Run %&r", false, 2)] // Ctrl+Alt+R
    public static void BuildAndRun()
    {
        ExecuteBuildSetup(BuildOptions.AutoRunPlayer);
    }

    #endregion

    #region Core Logic

    /// <summary>
    /// Validates the project setup and prepares the destination environment before launching the versioning interface.
    /// </summary>
    private static void ExecuteBuildSetup(BuildOptions additionalOptions)
    {
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

        if (!RunValidationChecks(target))
        {
            return;
        }

        string startPath = EditorPrefs.GetString(PrefKey, DefaultStartPath);
        if (!Directory.Exists(startPath))
        {
            Directory.CreateDirectory(DefaultStartPath);
            startPath = DefaultStartPath;
        }

        string folderPath = EditorUtility.SaveFolderPanel("Select Build Destination Folder", startPath, "");
        if (string.IsNullOrEmpty(folderPath))
        {
            Debug.LogWarning("Build cancelled. Don't waste my time.");
            return;
        }

        EditorPrefs.SetString(PrefKey, folderPath);

        OutVersionBumperWindow.ShowWindow(folderPath, additionalOptions, target);
    }

    /// <summary>
    /// Executes the final compilation pipeline once versioning configurations are locked in.
    /// </summary>
    public static void PerformFinalBuild(string folderPath, BuildOptions additionalOptions, BuildTarget target, bool deployWireless, string deviceIp, bool openFolder)
    {
        BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;

        BuildPlayerOptions buildOptions = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            target = target,
            targetGroup = targetGroup,
            options = additionalOptions
        };

        string packageName = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup));
        string extension = GetExtension(buildOptions.target);

        RunOldBuildCleanup(folderPath, packageName, extension);

        string fileName = GenerateBuildName(targetGroup);
        buildOptions.locationPathName = Path.Combine(folderPath, fileName + extension);

#if UNITY_6000_0_OR_NEWER
        UnityEditor.Build.Profile.BuildProfile activeProfile = UnityEditor.Build.Profile.BuildProfile.GetActiveBuildProfile();
        if (activeProfile != null)
        {
            Debug.Log($"[Unity 6] Compiling via Active Build Profile: {activeProfile.name}");
        }
#endif

        Debug.Log($"Starting automated build. Target path: {buildOptions.locationPathName}");
        BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build succeeded. File is sitting at: {buildOptions.locationPathName}");

            if (openFolder)
            {
                EditorUtility.RevealInFinder(buildOptions.locationPathName);
            }

            if (deployWireless && target == BuildTarget.Android)
            {
                DeployToWirelessDevice(buildOptions.locationPathName, packageName, deviceIp);
            }
        }
        else
        {
            Debug.LogError("Build failed. Fix your broken code.");
        }
    }

    #endregion

    #region Wireless Deployment

    /// <summary>
    /// Data structure holding the raw ADB connection ID and the human-readable model name.
    /// </summary>
    public struct AdbDevice
    {
        public string Id;
        public string DisplayName;
    }

    /// <summary>
    /// Authenticates a new Android 11+ device using a wireless pairing code.
    /// </summary>
    public static void PairWirelessDevice(string ipAndPort, string pairingCode)
    {
        string adbPath = GetAdbPath();
        if (!File.Exists(adbPath))
        {
            Debug.LogError($"[ADB] Executable missing at {adbPath}. Cannot pair.");
            return;
        }

        Debug.Log($"[ADB] Sending pairing request to {ipAndPort} with code {pairingCode}...");
        ExecuteAdbCommand(adbPath, $"pair {ipAndPort} {pairingCode}");
        Debug.Log("[ADB] If pairing was successful, refresh your devices and connect using the MAIN connection port.");
    }

    /// <summary>
    /// Manually attempts to connect to a target IP via ADB.
    /// </summary>
    public static void ConnectDevice(string ipAddress)
    {
        string adbPath = GetAdbPath();
        if (!File.Exists(adbPath))
        {
            Debug.LogError($"[ADB] Executable missing at {adbPath}. Cannot connect.");
            return;
        }

        Debug.Log($"[ADB] Attempting to connect to {ipAddress}...");
        ExecuteAdbCommand(adbPath, $"connect {ipAddress}");
    }

    /// <summary>
    /// Fetches a list of currently connected ADB devices, parsing the long output for device models.
    /// </summary>
    public static AdbDevice[] GetConnectedDevices()
    {
        string adbPath = GetAdbPath();
        if (!File.Exists(adbPath)) return new AdbDevice[0];

        List<AdbDevice> devices = new List<AdbDevice>();
        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = adbPath;
            process.StartInfo.Arguments = "devices -l";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            string[] lines = output.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (line.StartsWith("List") || string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1] == "device")
                {
                    string id = parts[0];
                    string model = "Unknown Model";

                    foreach (string part in parts)
                    {
                        if (part.StartsWith("model:"))
                        {
                            model = part.Substring(6).Replace("_", " ");
                            break;
                        }
                    }

                    devices.Add(new AdbDevice { Id = id, DisplayName = $"{model} ({id})" });
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ADB Error] {ex.Message}");
        }
        return devices.ToArray();
    }

    /// <summary>
    /// Pushes the compiled APK over Wi-Fi utilizing Unity's internal ADB tools to a specific target device.
    /// </summary>
    private static void DeployToWirelessDevice(string apkPath, string packageName, string ipAddress)
    {
        string adbPath = GetAdbPath();
        if (!File.Exists(adbPath))
        {
            Debug.LogError($"[Wireless Deploy] ADB executable not found at {adbPath}. Ensure the Android SDK is installed via Unity Hub.");
            return;
        }

        Debug.Log($"[Wireless Deploy] Initiating connection to {ipAddress}...");
        ExecuteAdbCommand(adbPath, $"connect {ipAddress}");

        Debug.Log($"[Wireless Deploy] Pushing APK to device {ipAddress}. This might take a moment...");
        ExecuteAdbCommand(adbPath, $"-s {ipAddress} install -r \"{apkPath}\"");

        Debug.Log($"[Wireless Deploy] Launching {packageName}...");
        ExecuteAdbCommand(adbPath, $"-s {ipAddress} shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1");
    }

    public static string GetAdbPath()
    {
        string sdkPath = EditorPrefs.GetString("AndroidSdkRoot");

        if (string.IsNullOrEmpty(sdkPath) || !Directory.Exists(sdkPath))
        {
            sdkPath = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK");
        }

        string extension = Application.platform == RuntimePlatform.WindowsEditor ? ".exe" : "";
        return Path.Combine(sdkPath, "platform-tools", $"adb{extension}");
    }

    private static void ExecuteAdbCommand(string adbPath, string arguments)
    {
        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = adbPath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output)) Debug.Log($"[ADB Output] {output}");
            if (!string.IsNullOrWhiteSpace(error) && !error.Contains("daemon started"))
                Debug.LogWarning($"[ADB Warning] {error}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ADB Execution Error] {ex.Message}");
        }
    }

    #endregion

    #region Validation & Cleanup

    private static bool RunValidationChecks(BuildTarget target)
    {
        if (GetEnabledScenes().Length == 0)
        {
            Debug.LogError("[Build Validation Failed] No scenes are enabled in your Build Settings window. Aborting build.");
            return false;
        }

        if (target == BuildTarget.Android)
        {
            if (PlayerSettings.Android.useCustomKeystore)
            {
                if (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) || string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
                {
                    Debug.LogWarning("[Build Validation Warning] Custom keystore is enabled, but passwords appear unassigned in this session.");
                }
            }
        }

        return true;
    }

    public static void ManualOldBuildCleanup(string folderPath)
    {
        BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
        string packageName = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup));
        string extension = GetExtension(EditorUserBuildSettings.activeBuildTarget);
        RunOldBuildCleanup(folderPath, packageName, extension);
    }

    private static void RunOldBuildCleanup(string folderPath, string packageName, string extension)
    {
        if (!Directory.Exists(folderPath) || string.IsNullOrEmpty(packageName)) return;

        string[] existingFiles = Directory.GetFiles(folderPath, $"*{packageName}_v*{extension}");
        foreach (string file in existingFiles)
        {
            try
            {
                File.Delete(file);
                Debug.Log($"[Cleanup] Cleared legacy build file: {Path.GetFileName(file)}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Cleanup Failed] Could not remove {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    #endregion

    #region Utility Methods

    private static string GenerateBuildName(BuildTargetGroup targetGroup)
    {
        string packageName = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup));
        string versionNumber = PlayerSettings.bundleVersion;
        string versionCode = "0";

        if (targetGroup == BuildTargetGroup.Android)
            versionCode = PlayerSettings.Android.bundleVersionCode.ToString();
        else if (targetGroup == BuildTargetGroup.iOS)
            versionCode = PlayerSettings.iOS.buildNumber;

        return $"{packageName}_v{versionNumber}_{versionCode}";
    }

    private static string GetExtension(BuildTarget target)
    {
        switch (target)
        {
            case BuildTarget.Android:
                return EditorUserBuildSettings.buildAppBundle ? ".aab" : ".apk";
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return ".exe";
            case BuildTarget.StandaloneOSX:
                return ".app";
            default:
                return "";
        }
    }

    private static string[] GetEnabledScenes()
    {
        var scenes = EditorBuildSettings.scenes;
        var enabledScenes = new List<string>();

        foreach (var scene in scenes)
        {
            if (scene.enabled)
            {
                enabledScenes.Add(scene.path);
            }
        }
        return enabledScenes.ToArray();
    }

    #endregion
}

/// <summary>
/// Modal dialog window handling fine-tuned adjustments to application version strings before deployment.
/// </summary>
public class OutVersionBumperWindow : EditorWindow
{
    private string _folderPath;
    private BuildOptions _options;
    private BuildTarget _target;

    private string _currentVersionString;
    private string _initialVersionString;
    private bool _focusRequested = true;
    private Vector2 _scrollPosition;

    #region Build Options State

    private bool _isDevelopmentBuild;
    private bool _openFolderOnComplete = true;

    #endregion

    #region Wireless State

    private bool _deployWireless;
    private string _deviceIp;

    private OutBuildAutomator.AdbDevice[] _connectedDevices = new OutBuildAutomator.AdbDevice[0];
    private string[] _deviceDisplayNames = new string[0];
    private int _selectedDeviceIndex = 0;

    private bool _showPairingMenu = false;
    private string _pairingIpPort = "192.168.0.X:PORT";
    private string _pairingCode = "123456";

    #endregion

    #region Device History State

    [System.Serializable]
    private class DeviceHistoryEntry
    {
        public string Id;
        public string DisplayName;
        public long LastSeenTicks;
    }

    [System.Serializable]
    private class DeviceHistoryWrapper
    {
        public List<DeviceHistoryEntry> Devices = new List<DeviceHistoryEntry>();
    }

    #endregion

    #region History Tracking States

    private bool _hasPrevChange;
    private string _prevVersionFrom;
    private string _prevVersionTo;
    private int _prevCodeFrom;
    private int _prevCodeTo;

    #endregion

    /// <summary>
    /// Instantiates and configures the modal display window.
    /// </summary>
    public static void ShowWindow(string folderPath, BuildOptions options, BuildTarget target)
    {
        OutVersionBumperWindow window = GetWindow<OutVersionBumperWindow>(true, "Version Control Hub", true);
        window._folderPath = folderPath;
        window._options = options;
        window._target = target;

        window._initialVersionString = PlayerSettings.bundleVersion;
        window._currentVersionString = window._initialVersionString;

        // Removed maxSize to allow resizing. Set a comfortable minimum size.
        window.minSize = new Vector2(450, 520);
        window._focusRequested = true;

        window.ShowUtility();
    }

    private void OnEnable()
    {
        _hasPrevChange = EditorPrefs.GetBool("OutBuildAutomator_HasPrev", false);
        if (_hasPrevChange)
        {
            _prevVersionFrom = EditorPrefs.GetString("OutBuildAutomator_PrevVerFrom", "0.0");
            _prevVersionTo = EditorPrefs.GetString("OutBuildAutomator_PrevVerTo", "0.0");
            _prevCodeFrom = EditorPrefs.GetInt("OutBuildAutomator_PrevCodeFrom", 0);
            _prevCodeTo = EditorPrefs.GetInt("OutBuildAutomator_PrevCodeTo", 0);
        }

        _deployWireless = EditorPrefs.GetBool("OutBuildAutomator_WirelessDeploy", false);
        _deviceIp = EditorPrefs.GetString("OutBuildAutomator_DeviceIP", "192.168.1.X:5555");
        _isDevelopmentBuild = EditorPrefs.GetBool("OutBuildAutomator_IsDevBuild", false);
        _openFolderOnComplete = EditorPrefs.GetBool("OutBuildAutomator_OpenFolder", true);

        RefreshDeviceList();
    }

    private void RefreshDeviceList()
    {
        string historyJson = EditorPrefs.GetString("OutBuildAutomator_DeviceHistory", "{}");
        DeviceHistoryWrapper history = JsonUtility.FromJson<DeviceHistoryWrapper>(historyJson) ?? new DeviceHistoryWrapper();
        if (history.Devices == null) history.Devices = new List<DeviceHistoryEntry>();

        OutBuildAutomator.AdbDevice[] currentDevices = OutBuildAutomator.GetConnectedDevices();
        long currentTicks = System.DateTime.UtcNow.Ticks;
        long threeDaysTicks = System.TimeSpan.FromDays(3).Ticks;

        foreach (var d in currentDevices)
        {
            var existing = history.Devices.Find(x => x.Id == d.Id);
            if (existing != null)
            {
                existing.DisplayName = d.DisplayName;
                existing.LastSeenTicks = currentTicks;
            }
            else
            {
                history.Devices.Add(new DeviceHistoryEntry { Id = d.Id, DisplayName = d.DisplayName, LastSeenTicks = currentTicks });
            }
        }

        history.Devices.RemoveAll(x => (currentTicks - x.LastSeenTicks) > threeDaysTicks);
        EditorPrefs.SetString("OutBuildAutomator_DeviceHistory", JsonUtility.ToJson(history));

        _connectedDevices = new OutBuildAutomator.AdbDevice[history.Devices.Count];
        _deviceDisplayNames = new string[history.Devices.Count];

        for (int i = 0; i < history.Devices.Count; i++)
        {
            _connectedDevices[i] = new OutBuildAutomator.AdbDevice { Id = history.Devices[i].Id, DisplayName = history.Devices[i].DisplayName };

            bool isActive = System.Array.Exists(currentDevices, x => x.Id == history.Devices[i].Id);
            string status = isActive ? "[Active] " : "[History] ";
            _deviceDisplayNames[i] = status + history.Devices[i].DisplayName;
        }

        if (_selectedDeviceIndex >= _connectedDevices.Length) _selectedDeviceIndex = 0;
    }

    private void PurgeOldPortsForIp(string targetIpAndPort)
    {
        // Ignore if it's a malformed IP string
        if (string.IsNullOrEmpty(targetIpAndPort) || !targetIpAndPort.Contains(":")) return;

        // Extract just the base IP (e.g., "192.168.1.55") and append the colon for strict matching
        string baseIp = targetIpAndPort.Split(':')[0];
        string matchPrefix = baseIp + ":";

        string historyJson = EditorPrefs.GetString("OutBuildAutomator_DeviceHistory", "{}");
        DeviceHistoryWrapper history = JsonUtility.FromJson<DeviceHistoryWrapper>(historyJson) ?? new DeviceHistoryWrapper();

        if (history.Devices == null || history.Devices.Count == 0) return;

        // Nuke any history entry that matches the IP but has a different port
        int removedCount = history.Devices.RemoveAll(d => d.Id.StartsWith(matchPrefix) && d.Id != targetIpAndPort);

        if (removedCount > 0)
        {
            EditorPrefs.SetString("OutBuildAutomator_DeviceHistory", JsonUtility.ToJson(history));
            Debug.Log($"[History Cleanup] Purged {removedCount} outdated port record(s) for {baseIp}. Keeping the dropdown clean.");
        }
    }

    private void OnGUI()
    {
        if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
        {
            ConfirmAndCompile();
            Event.current.Use();
        }

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        GUILayout.Space(12);
        EditorGUILayout.LabelField("Review Project Versioning", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Modify the active release version tracking values below:", EditorStyles.wordWrappedLabel);
        GUILayout.Space(10);

        #region Past Modifications Display

        if (_hasPrevChange)
        {
            EditorGUILayout.BeginVertical("helpBox");
            GUI.contentColor = new Color(0.85f, 0.85f, 0.85f);
            EditorGUILayout.LabelField("Previous Pipeline Run Version Tracking:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"• Version String: {_prevVersionFrom} ➔ {_prevVersionTo}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"• Version Code:   {_prevCodeFrom} ➔ {_prevCodeTo}", EditorStyles.miniLabel);
            GUI.contentColor = Color.white;
            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }

        #endregion

        EditorGUILayout.BeginHorizontal("box");
        EditorGUILayout.LabelField("Target Version String:", GUILayout.Width(130));
        _currentVersionString = EditorGUILayout.TextField(_currentVersionString);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        // Row 1: Sub/Patch Version Control
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Increment Sub-Version (+0.01)", GUILayout.Height(24)))
        {
            ModifyVersionSegment(lastIndex: true, amount: 1);
        }
        if (GUILayout.Button("Decrement Sub-Version (-0.01)", GUILayout.Height(24)))
        {
            ModifyVersionSegment(lastIndex: true, amount: -1);
        }
        EditorGUILayout.EndHorizontal();

        // Row 2: Main/Minor Version Control
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Increment Main-Minor (+0.1)", GUILayout.Height(24)))
        {
            ModifyVersionSegment(lastIndex: false, amount: 1);
        }
        if (GUILayout.Button("Reset to Original Initial", GUILayout.Height(24)))
        {
            _currentVersionString = _initialVersionString;
        }
        EditorGUILayout.EndHorizontal();

        #region Build Configurations

        GUILayout.Space(10);
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Build Configurations", EditorStyles.boldLabel);
        _isDevelopmentBuild = EditorGUILayout.ToggleLeft(" Development Build", _isDevelopmentBuild);
        _openFolderOnComplete = EditorGUILayout.ToggleLeft(" Open Destination Folder on Success", _openFolderOnComplete);

        GUILayout.Space(5);
        if (GUILayout.Button("Manually Purge Old Builds in Directory", GUILayout.Height(20)))
        {
            OutBuildAutomator.ManualOldBuildCleanup(_folderPath);
        }
        EditorGUILayout.EndVertical();

        #endregion

        #region Wireless Configurations

        GUILayout.Space(10);
        EditorGUILayout.BeginVertical("box");
        _deployWireless = EditorGUILayout.ToggleLeft(" Push Build to Wireless Device (ADB)", _deployWireless, EditorStyles.boldLabel);
        if (_deployWireless)
        {
            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Devices", GUILayout.Width(110)))
            {
                RefreshDeviceList();
                GUI.FocusControl(null);
            }

            if (_connectedDevices != null && _connectedDevices.Length > 0)
            {
                EditorGUI.BeginChangeCheck();
                _selectedDeviceIndex = EditorGUILayout.Popup(_selectedDeviceIndex, _deviceDisplayNames);

                // Only overwrite the IP field if you actually clicked and changed the dropdown
                if (EditorGUI.EndChangeCheck() && _selectedDeviceIndex >= 0 && _selectedDeviceIndex < _connectedDevices.Length)
                {
                    _deviceIp = _connectedDevices[_selectedDeviceIndex].Id;

                    // Force Unity to drop focus from the text box so the new IP renders immediately
                    GUI.FocusControl(null);
                }
            }
            else
            {
                EditorGUILayout.LabelField("No active or historical ADB devices detected.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target IP & Port:", GUILayout.Width(110));
            _deviceIp = EditorGUILayout.TextField(_deviceIp);

            if (GUILayout.Button("Connect", GUILayout.Width(70)))
            {
                PurgeOldPortsForIp(_deviceIp);
                OutBuildAutomator.ConnectDevice(_deviceIp);
                RefreshDeviceList();
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);
            _showPairingMenu = EditorGUILayout.Foldout(_showPairingMenu, "🔑 Pair New Device (Android 11+)");
            if (_showPairingMenu)
            {
                EditorGUILayout.BeginVertical("helpBox");
                EditorGUILayout.LabelField("1. On device: Developer Options > Wireless Debugging > Pair device with pairing code.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("2. Enter the exact IP:Port and 6-digit code shown on the popup screen.", EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Pairing IP & Port:", GUILayout.Width(110));
                _pairingIpPort = EditorGUILayout.TextField(_pairingIpPort);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("6-Digit Code:", GUILayout.Width(110));
                _pairingCode = EditorGUILayout.TextField(_pairingCode);
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Execute ADB Pair", GUILayout.Height(24)))
                {
                    OutBuildAutomator.PairWirelessDevice(_pairingIpPort, _pairingCode);
                    GUI.FocusControl(null);
                }

                GUI.contentColor = new Color(1f, 0.4f, 0.4f);
                EditorGUILayout.LabelField("Note: The Pairing Port is DIFFERENT than the Connection Port!", EditorStyles.boldLabel);
                GUI.contentColor = Color.white;

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.LabelField("Ensure Developer Options & Wireless Debugging are active on the targeted device.", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndVertical();

        #endregion

        GUILayout.Space(15);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.2f, 0.6f, 0.2f);

        GUI.SetNextControlName("ConfirmButton");
        if (GUILayout.Button("Confirm & Compile Build", GUILayout.Height(30)))
        {
            ConfirmAndCompile();
        }

        if (_focusRequested)
        {
            GUI.FocusControl("ConfirmButton");
            _focusRequested = false;
        }

        GUI.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
        if (GUILayout.Button("Abort", GUILayout.Height(30), GUILayout.Width(80)))
        {
            Debug.LogWarning("Build cycle aborted via version selector control window.");
            Close();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void ConfirmAndCompile()
    {
        int oldCode = 0;
        int newCode = 0;
        BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;

        if (targetGroup == BuildTargetGroup.Android)
        {
            oldCode = PlayerSettings.Android.bundleVersionCode;
            newCode = oldCode;
        }
        else if (targetGroup == BuildTargetGroup.iOS)
        {
            int.TryParse(PlayerSettings.iOS.buildNumber, out oldCode);
            newCode = oldCode;
        }

        int codeDelta = CalculateVersionDelta(_initialVersionString, _currentVersionString);

        if (codeDelta > 0)
        {
            newCode = oldCode + codeDelta;

            if (targetGroup == BuildTargetGroup.Android)
            {
                PlayerSettings.Android.bundleVersionCode = newCode;
            }
            else if (targetGroup == BuildTargetGroup.iOS)
            {
                PlayerSettings.iOS.buildNumber = newCode.ToString();
            }
        }

        // Sync local preferences
        EditorPrefs.SetBool("OutBuildAutomator_HasPrev", true);
        EditorPrefs.SetString("OutBuildAutomator_PrevVerFrom", _initialVersionString);
        EditorPrefs.SetString("OutBuildAutomator_PrevVerTo", _currentVersionString);
        EditorPrefs.SetInt("OutBuildAutomator_PrevCodeFrom", oldCode);
        EditorPrefs.SetInt("OutBuildAutomator_PrevCodeTo", newCode);

        // Save states
        EditorPrefs.SetBool("OutBuildAutomator_WirelessDeploy", _deployWireless);
        EditorPrefs.SetString("OutBuildAutomator_DeviceIP", _deviceIp);
        EditorPrefs.SetBool("OutBuildAutomator_IsDevBuild", _isDevelopmentBuild);
        EditorPrefs.SetBool("OutBuildAutomator_OpenFolder", _openFolderOnComplete);

        PlayerSettings.bundleVersion = _currentVersionString;

        if (_isDevelopmentBuild)
        {
            _options |= BuildOptions.Development;
        }
        else
        {
            _options &= ~BuildOptions.Development;
        }

        if (_deployWireless)
        {
            _options &= ~BuildOptions.AutoRunPlayer;
        }

        Close();
        OutBuildAutomator.PerformFinalBuild(_folderPath, _options, _target, _deployWireless, _deviceIp, _openFolderOnComplete);
    }

    private void ModifyVersionSegment(bool lastIndex, int amount)
    {
        string[] tokens = _currentVersionString.Split('.');
        if (tokens.Length == 0) return;

        int targetIndex = lastIndex ? tokens.Length - 1 : Mathf.Max(0, tokens.Length - 2);

        if (int.TryParse(tokens[targetIndex], out int numericValue))
        {
            numericValue += amount;
            if (numericValue < 0) numericValue = 0;
            tokens[targetIndex] = numericValue.ToString();

            if (amount > 0 && !lastIndex && tokens.Length > 1)
            {
                for (int i = targetIndex + 1; i < tokens.Length; i++)
                {
                    if (int.TryParse(tokens[i], out _)) tokens[i] = "0";
                }
            }

            string processedString = string.Join(".", tokens);

            if (EvaluateVersionDeficit(processedString, _initialVersionString))
            {
                _currentVersionString = _initialVersionString;
            }
            else
            {
                _currentVersionString = processedString;
            }
        }
    }

    private bool EvaluateVersionDeficit(string currentCandidate, string fallbackFloor)
    {
        string[] currentTokens = currentCandidate.Split('.');
        string[] floorTokens = fallbackFloor.Split('.');

        for (int i = 0; i < Mathf.Min(currentTokens.Length, floorTokens.Length); i++)
        {
            if (int.TryParse(currentTokens[i], out int currentNum) && int.TryParse(floorTokens[i], out int floorNum))
            {
                if (currentNum < floorNum) return true;
                if (currentNum > floorNum) return false;
            }
        }
        return currentTokens.Length < floorTokens.Length;
    }

    private int CalculateVersionDelta(string initial, string current)
    {
        if (initial == current) return 0;

        string[] initTokens = initial.Split('.');
        string[] currTokens = current.Split('.');

        for (int i = 0; i < Mathf.Min(initTokens.Length, currTokens.Length); i++)
        {
            if (initTokens[i] != currTokens[i])
            {
                if (int.TryParse(initTokens[i], out int initVal) && int.TryParse(currTokens[i], out int currVal))
                {
                    int difference = currVal - initVal;
                    if (difference <= 0) return 0;
                    return difference;
                }
            }
        }
        return currTokens.Length > initTokens.Length ? 1 : 0;
    }
}
#endif