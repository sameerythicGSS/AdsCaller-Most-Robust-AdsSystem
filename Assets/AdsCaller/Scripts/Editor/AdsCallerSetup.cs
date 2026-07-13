#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Auto-launching setup wizard for Ads Caller components. Handles scene injection,
/// automated package installation, and Addressables configuration via reflection.
/// </summary>
[InitializeOnLoad]
public class AdsCallerSetup : EditorWindow
{
    // Converted to project-specific keys to ensure the setup triggers properly in new projects
    private static string PrefsSetupDone => $"OutAdsCaller_SetupDone_{Application.dataPath.GetHashCode()}";
    private static string PrefsResumePhase => $"OutAdsCaller_ResumePhase_{Application.dataPath.GetHashCode()}";
    private static string PrefsTargetScene => $"OutAdsCaller_TargetScene_{Application.dataPath.GetHashCode()}";
    private static string PrefsCrossPromo => $"OutAdsCaller_CrossPromo_{Application.dataPath.GetHashCode()}";

    private Vector2 _scrollPos;
    private List<EditorBuildSettingsScene> _buildScenes = new List<EditorBuildSettingsScene>();
    private int _selectedSceneIndex = -1;

    private bool _includeMainAdsCaller = true;
    private bool _includeCrossPromoAddon = false;

    private static AddRequest _addressablesRequest;
    private static AddRequest _newtonsoftRequest;
    private static bool _isWaitingForPackages = false;

    #region Window Setup & Auto-Launch

    static AdsCallerSetup()
    {
        EditorApplication.delayCall += () =>
        {
            // Catches the script after a domain reload triggered by package installations
            if (EditorPrefs.GetInt(PrefsResumePhase, 0) == 1)
            {
                EditorPrefs.SetInt(PrefsResumePhase, 0);
                ExecutePostPackageSetup();
                return;
            }

            if (!EditorPrefs.GetBool(PrefsSetupDone, false))
            {
                string savedScenePath = EditorPrefs.GetString(PrefsTargetScene, "");

                if (string.IsNullOrEmpty(savedScenePath))
                {
                    ShowWindow();
                    return;
                }

                if (VerifySceneConfigured(savedScenePath))
                {
                    EditorPrefs.SetBool(PrefsSetupDone, true);
                    Debug.Log($"[OutAdsSetup] Verified intact Ads setup in {Path.GetFileNameWithoutExtension(savedScenePath)}. Staying quiet.");
                }
                else
                {
                    Debug.LogWarning($"[OutAdsSetup] Saved scene {Path.GetFileNameWithoutExtension(savedScenePath)} is missing essential ad prefabs. Re-opening setup wizard.");
                    ShowWindow();
                }
            }
        };
    }

    /// <summary>
    /// Loads the target scene additively in the background, checks for the essential prefabs, and unloads it.
    /// </summary>
    private static bool VerifySceneConfigured(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath)) return false;

        Scene sceneToVerify = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
        bool wasAlreadyLoaded = sceneToVerify.isLoaded;

        if (!wasAlreadyLoaded)
        {
            try
            {
                sceneToVerify = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }
            catch
            {
                return false;
            }
        }

        GameObject[] roots = sceneToVerify.GetRootGameObjects();

        bool hasFb = false, hasMediator = false, hasSE = false;
        bool hasMainAds = false, hasCrossPromoAds = false, hasCatalog = false;

        foreach (GameObject go in roots)
        {
            if (go.name == "FbAnalytics") hasFb = true;
            if (go.name == "AdsMediator") hasMediator = true;
            if (go.name == "SEHandler") hasSE = true;

            if (go.name == "AdsCaller") hasMainAds = true;
            if (go.name == "AdsCallerSalon") hasCrossPromoAds = true;
            if (go.name == "CatalogLoader") hasCatalog = true;
        }

        bool hasEssentials = hasFb && hasMediator && hasSE;
        bool hasPayload = hasMainAds || (hasCrossPromoAds && hasCatalog);

        if (!wasAlreadyLoaded)
        {
            EditorSceneManager.CloseScene(sceneToVerify, true);
        }

        return hasEssentials && hasPayload;
    }

    [MenuItem("Ads Caller/Setup Wizard")]
    public static void ShowWindow()
    {
        AdsCallerSetup window = GetWindow<AdsCallerSetup>(true, "Setup Ads Caller", true);
        window.minSize = new Vector2(450, 500);
        window.RefreshScenes();
        window.ShowUtility();
    }

    private void RefreshScenes()
    {
        _buildScenes.Clear();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled) _buildScenes.Add(scene);
        }
    }

    #endregion

    #region GUI Draw

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Ads Caller Initialization Wizard", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select a target scene from your Build Settings to inject the core Ad network prefabs and handlers.", MessageType.Info);
        GUILayout.Space(10);

        EditorGUILayout.LabelField("Available Build Scenes:", EditorStyles.boldLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, "box", GUILayout.Height(150));
        if (_buildScenes.Count == 0)
        {
            EditorGUILayout.LabelField("No enabled scenes found in Build Settings.", EditorStyles.miniLabel);
        }
        else
        {
            for (int i = 0; i < _buildScenes.Count; i++)
            {
                string sceneName = Path.GetFileNameWithoutExtension(_buildScenes[i].path);
                bool isSelected = (_selectedSceneIndex == i);

                GUI.backgroundColor = isSelected ? new Color(0.3f, 0.6f, 0.9f) : Color.white;
                if (GUILayout.Button(sceneName, EditorStyles.toolbarButton))
                {
                    _selectedSceneIndex = i;
                }
                GUI.backgroundColor = Color.white;
            }
        }
        EditorGUILayout.EndScrollView();

        GUILayout.Space(15);

        EditorGUILayout.BeginVertical("box");
        _includeMainAdsCaller = EditorGUILayout.ToggleLeft(" Include Main Ads Caller (AdsCaller.prefab)", _includeMainAdsCaller, EditorStyles.boldLabel);
        GUILayout.Space(5);
        _includeCrossPromoAddon = EditorGUILayout.ToggleLeft(" Include Cross Promotion Addon (SalonAds)", _includeCrossPromoAddon, EditorStyles.boldLabel);
        if (_includeCrossPromoAddon)
        {
            EditorGUILayout.LabelField("  ↳ Will inject AdsCallerSalon & CatalogLoader", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  ↳ Will auto-install Addressables & Newtonsoft JSON", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        GUI.enabled = _selectedSceneIndex >= 0 && !_isWaitingForPackages;
        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
        if (GUILayout.Button(_isWaitingForPackages ? "Installing Packages..." : "Execute Setup", GUILayout.Height(40)))
        {
            ExecuteSetup();
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        if (GUILayout.Button("Dismiss (Don't show again)", EditorStyles.miniButton))
        {
            EditorPrefs.SetBool(PrefsSetupDone, true);
            Close();
        }
    }

    #endregion

    #region Execution Logic

    /// <summary>
    /// Executes the prefab injection and package installation workflow.
    /// </summary>
    private void ExecuteSetup()
    {
        string targetScenePath = _buildScenes[_selectedSceneIndex].path;

        if (VerifySceneConfigured(targetScenePath))
        {
            if (!EditorUtility.DisplayDialog(
                "Scene Already Rigged",
                "It looks like this scene already contains the Ads Caller setup. Do you want to proceed and inject duplicates?",
                "Yes, Inject Anyway",
                "Cancel"))
            {
                return;
            }
        }

        Scene currentScene = EditorSceneManager.GetActiveScene();
        if (currentScene.path != targetScenePath)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(targetScenePath, OpenSceneMode.Single);
            }
            else
            {
                Debug.LogWarning("[OutAdsSetup] Setup aborted by user.");
                return;
            }
        }

        EditorPrefs.SetString(PrefsTargetScene, targetScenePath);
        EditorPrefs.SetBool(PrefsCrossPromo, _includeCrossPromoAddon);

        InjectPrefab("Assets/AdsCaller/Prefabs/FbAnalytics.prefab");
        GameObject mediatorInstance = InjectPrefab("Assets/AdsCaller/Prefabs/AdsMediator.prefab");
        InjectPrefab("Assets/MMPHandler/SEHandler.prefab");

        if (mediatorInstance != null)
        {
            ConfigureAdsMediator(mediatorInstance);
        }

        if (_includeMainAdsCaller)
        {
            InjectPrefab("Assets/AdsCaller/Prefabs/AdsCaller.prefab");
        }

        if (_includeCrossPromoAddon)
        {
            InjectPrefab("Assets/AdsCaller/SalonAds/Prefabs/AdsCallerSalon.prefab");
            InjectPrefab("Assets/AdsCaller/SalonAds/Prefabs/CatalogLoader.prefab");

            EditorSceneManager.SaveOpenScenes();
            InstallPackagesAsync();
        }
        else
        {
            EditorSceneManager.SaveOpenScenes();
            FinishSetup();
        }
    }

    private GameObject InjectPrefab(string assetPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab != null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, $"Injected {prefab.name}");
            Debug.Log($"[OutAdsSetup] Injected {prefab.name} into scene.");
            return instance;
        }

        Debug.LogError($"[OutAdsSetup] Missing Prefab at path: {assetPath}. Make sure the asset exists.");
        return null;
    }

    private void ConfigureAdsMediator(GameObject mediatorObj)
    {
        Component mediatorComponent = mediatorObj.GetComponent("AdsMediator");
        if (mediatorComponent == null)
        {
            Debug.LogError("[OutAdsSetup] AdsMediator component not found on injected prefab!");
            return;
        }

        SerializedObject so = new SerializedObject(mediatorComponent);

        SerializedProperty adsModeProp = so.FindProperty("adsMode");
        SerializedProperty testAdsProp = so.FindProperty("testAds");
        SerializedProperty bypassRewardedProp = so.FindProperty("bypassRewardedAd");
        SerializedProperty useAdsCallerRewardedProp = so.FindProperty("useAdsCallerRewarded");

        if (_includeCrossPromoAddon && _includeMainAdsCaller)
        {
            if (adsModeProp != null) adsModeProp.enumValueIndex = GetEnumIndex(adsModeProp, "CrossPromo");
            if (testAdsProp != null) testAdsProp.boolValue = false;
            if (bypassRewardedProp != null) bypassRewardedProp.boolValue = false;
            if (useAdsCallerRewardedProp != null) useAdsCallerRewardedProp.boolValue = true;
            Debug.Log("[OutAdsSetup] AdsMediator configured for dual functionality.");
        }
        else if (_includeCrossPromoAddon)
        {
            if (adsModeProp != null) adsModeProp.enumValueIndex = GetEnumIndex(adsModeProp, "CrossPromo");
            if (testAdsProp != null) testAdsProp.boolValue = false;
            if (bypassRewardedProp != null) bypassRewardedProp.boolValue = false;
            if (useAdsCallerRewardedProp != null) useAdsCallerRewardedProp.boolValue = false;
            Debug.Log("[OutAdsSetup] AdsMediator configured for Cross Promotion.");
        }
        else if (_includeMainAdsCaller)
        {
            if (adsModeProp != null) adsModeProp.enumValueIndex = GetEnumIndex(adsModeProp, "Online");
            if (testAdsProp != null) testAdsProp.boolValue = false;
            if (bypassRewardedProp != null) bypassRewardedProp.boolValue = false;
            if (useAdsCallerRewardedProp != null) useAdsCallerRewardedProp.boolValue = true;
            Debug.Log("[OutAdsSetup] AdsMediator configured for Online Main Ads Caller.");
        }

        so.ApplyModifiedProperties();
    }

    private int GetEnumIndex(SerializedProperty prop, string enumName)
    {
        if (prop == null || prop.enumNames == null) return 0;
        for (int i = 0; i < prop.enumNames.Length; i++)
        {
            if (prop.enumNames[i] == enumName) return i;
        }
        return 0;
    }

    #endregion

    #region Package Management

    private void InstallPackagesAsync()
    {
        _isWaitingForPackages = true;
        Debug.Log("[OutAdsSetup] Requesting Package Installations...");

        // Set the flag BEFORE starting the request. Package compilation will cause a domain reload
        // which wipes static variables. Setting this here ensures we survive the reload.
        EditorPrefs.SetInt(PrefsResumePhase, 1);

        _addressablesRequest = Client.Add("com.unity.addressables");
        _newtonsoftRequest = Client.Add("com.unity.nuget.newtonsoft-json");

        EditorApplication.update += PackageInstallationTracker;
    }

    private static void PackageInstallationTracker()
    {
        // If requests are null, a domain reload happened and wiped them. We can drop tracking safely.
        if (_addressablesRequest == null || _newtonsoftRequest == null)
        {
            EditorApplication.update -= PackageInstallationTracker;
            return;
        }

        if (_addressablesRequest.IsCompleted && _newtonsoftRequest.IsCompleted)
        {
            EditorApplication.update -= PackageInstallationTracker;
            _isWaitingForPackages = false;

            if (_addressablesRequest.Status == StatusCode.Success && _newtonsoftRequest.Status == StatusCode.Success)
            {
                // Trigger setup if domain reload somehow didn't wipe the tracker
                if (EditorPrefs.GetInt(PrefsResumePhase, 0) == 1)
                {
                    EditorPrefs.SetInt(PrefsResumePhase, 0);
                    ExecutePostPackageSetup();
                }
            }
            else
            {
                Debug.LogError("[OutAdsSetup] Package installation failed. Check Package Manager console.");
                EditorUtility.DisplayDialog("Setup Error", "Failed to install required packages. Setup halted.", "OK");
                EditorPrefs.SetInt(PrefsResumePhase, 0);
            }
        }
    }

    #endregion

    #region Addressables & Completion

    private static void ExecutePostPackageSetup()
    {
        CreateAddressablesGroupViaReflection();
        FinishSetup();
    }

    /// <summary>
    /// Uses reflection to bypass compiler errors that would occur before Addressables is installed.
    /// Replicates clicking "Create Addressables Group" by explicitly invoking the Create method.
    /// </summary>
    private static void CreateAddressablesGroupViaReflection()
    {
        try
        {
            Assembly addressablesAssembly = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "Unity.Addressables.Editor")
                {
                    addressablesAssembly = assembly;
                    break;
                }
            }

            if (addressablesAssembly == null)
            {
                Debug.LogWarning("[OutAdsSetup] Addressables assembly not found. Packages might not be fully loaded.");
                return;
            }

            Type defaultObjType = addressablesAssembly.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
            Type settingsType = addressablesAssembly.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");

            if (defaultObjType == null || settingsType == null) return;

            PropertyInfo settingsProp = defaultObjType.GetProperty("Settings", BindingFlags.Static | BindingFlags.Public);
            object settings = settingsProp?.GetValue(null);

            if (settings == null)
            {
                Debug.Log("[OutAdsSetup] Addressables Settings not found. Auto-creating default assets...");

                MethodInfo createMethod = settingsType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }, null);

                if (createMethod != null)
                {
                    settings = createMethod.Invoke(null, new object[] { "Assets/AddressableAssetsData", "AddressableAssetSettings", true, true });
                    settingsProp?.SetValue(null, settings);
                }
            }

            if (settings == null)
            {
                Debug.LogWarning("[OutAdsSetup] Addressables settings could not be generated. Please initialize manually.");
                return;
            }

            MethodInfo findGroupMethod = settingsType.GetMethod("FindGroup");
            object group = findGroupMethod?.Invoke(settings, new object[] { "Salon_AdTypes" });

            if (group == null)
            {
                MethodInfo createGroupMethod = settingsType.GetMethod("CreateGroup", new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(List<Type>), typeof(Type[]) });

                if (createGroupMethod != null)
                {
                    createGroupMethod.Invoke(settings, new object[] { "Salon_AdTypes", false, false, true, null, null });
                    Debug.Log("[OutAdsSetup] Salon_AdTypes Addressable Group auto-created.");
                }
            }
            else
            {
                Debug.Log("[OutAdsSetup] Salon_AdTypes Addressable Group already exists.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OutAdsSetup] Reflection failed during Addressables setup: {ex.Message}");
        }
    }

    private static void FinishSetup()
    {
        EditorPrefs.SetBool(PrefsSetupDone, true);

        string message = "The setup is done.";

        if (EditorPrefs.GetBool(PrefsCrossPromo, false))
        {
            message += "\n\nPlease manually change the database from AdsCaller/SalonAds/Database and replace the links in the Catalog Loader prefab injected in the scene.";
        }

        EditorUtility.DisplayDialog(
            "Ads Caller Setup Complete",
            message,
            "Got it"
        );

        if (HasOpenInstances<AdsCallerSetup>())
        {
            GetWindow<AdsCallerSetup>().Close();
        }
    }

    #endregion
}
#endif