#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

/// <summary>
/// MultiAgentSetup — Editor tool untuk setup Multi-Agent system di scene.
/// 
/// Menu: Tools > Equilibrew > Setup Multi-Agent System
/// 
/// Yang dilakukan:
/// 1. Buat MultiAgentBridge (DontDestroyOnLoad)
/// 2. Buat MultiAgentCustomerAdapter
/// 3. Buat MultiAgentFlowController
/// 4. Buat CurhatChoiceUI (Canvas panel)
/// 5. Wire references otomatis
/// 6. Disable CustomerManager lama (jika ada)
/// </summary>
public static class MultiAgentSetup
{
    [MenuItem("Tools/Equilibrew/Setup Multi-Agent System")]
    public static void SetupMultiAgentSystem()
    {
        Debug.Log("[MultiAgentSetup] Starting setup...");
        
        // 1. MultiAgentBridge (DontDestroyOnLoad)
        var bridge = FindOrCreate<MultiAgentBridge>("MultiAgentBridge");
        // Mark as DontDestroyOnLoad
        bridge.gameObject.isStatic = false;
        
        // 2. MultiAgentCustomerAdapter
        var adapter = FindOrCreate<MultiAgentCustomerAdapter>("MultiAgentCustomerAdapter");
        
        // 3. MultiAgentFlowController
        var flowController = FindOrCreate<MultiAgentFlowController>("MultiAgentFlowController");
        
        // Wire adapter reference
        flowController.adapter = adapter;
        
        // 4. CurhatChoiceUI
        var curhatUI = FindOrCreateCurhatUI();
        flowController.curhatChoiceUI = curhatUI;
        
        // 5. Find InkDialogController in scene
        var inkDialog = Object.FindObjectOfType<InkDialogController>();
        if (inkDialog != null)
        flowController.inkDialogController = inkDialog;
        
        // 6. Find RecipeValidator
        var recipeValidator = Object.FindObjectOfType<RecipeValidator>();
        if (recipeValidator != null)
            flowController.recipeValidator = recipeValidator;
        
        // 7. Find CupController
        var cupController = Object.FindObjectOfType<CupController>();
        if (cupController != null)
            flowController.cupController = cupController;
        
        // 8. Disable old CustomerManager
        var oldManager = Object.FindObjectOfType<CustomerManager>();
        if (oldManager != null)
        {
            oldManager.enabled = false;
            Debug.Log("[MultiAgentSetup] Disabled old CustomerManager.");
        }
        
        // 9. Mark scene dirty
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        
        Debug.Log("[MultiAgentSetup] Setup complete! Configure remaining references in Inspector.");
        EditorUtility.DisplayDialog("Multi-Agent Setup", 
            "Setup complete!\n\n" +
            "Remaining steps:\n" +
            "1. Assign CustomerProfile list in FlowController\n" +
            "2. Assign spawnParent and customerPrefab\n" +
            "3. Assign affinityWidget and ingredientPanelController\n" +
            "4. Start Python server: python 'Multi Agent/server.py'\n" +
            "5. Press Play!", "OK");
    }
    
    private static T FindOrCreate<T>(string name) where T : Component
    {
        var existing = Object.FindObjectOfType<T>();
        if (existing != null)
        {
            Debug.Log($"[MultiAgentSetup] Found existing {name}.");
            return existing;
        }
        
        var go = new GameObject(name);
        var comp = go.AddComponent<T>();
        Debug.Log($"[MultiAgentSetup] Created {name}.");
        return comp;
    }
    
    private static CurhatChoiceUI FindOrCreateCurhatUI()
    {
        var existing = Object.FindObjectOfType<CurhatChoiceUI>();
        if (existing != null)
        {
            Debug.Log("[MultiAgentSetup] Found existing CurhatChoiceUI.");
            return existing;
        }
        
        // Create Canvas for curhat UI
        var canvasGo = new GameObject("CurhatChoiceCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // Above other UI
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        
        // Panel root
        var panelGo = new GameObject("CurhatPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0, 20);
        panelRect.sizeDelta = new Vector2(600, 300);
        
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        
        var layoutGo = new GameObject("Layout");
        layoutGo.transform.SetParent(panelGo.transform, false);
        var layoutRect = layoutGo.AddComponent<RectTransform>();
        layoutRect.anchorMin = Vector2.zero;
        layoutRect.anchorMax = Vector2.one;
        layoutRect.offsetMin = new Vector2(10, 10);
        layoutRect.offsetMax = new Vector2(-10, -10);
        
        var vlg = layoutGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.LowerCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        // Choice button container
        var choiceContainer = new GameObject("ChoiceContainer");
        choiceContainer.transform.SetParent(layoutGo.transform, false);
        var choiceContainerRect = choiceContainer.AddComponent<RectTransform>();
        var choiceVlg = choiceContainer.AddComponent<VerticalLayoutGroup>();
        choiceVlg.spacing = 4;
        choiceVlg.childAlignment = TextAnchor.UpperCenter;
        choiceVlg.childControlWidth = true;
        choiceVlg.childControlHeight = false;
        choiceVlg.childForceExpandWidth = true;
        choiceVlg.childForceExpandHeight = false;
        var csf = choiceContainer.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Choice button prefab
        var btnPrefabGo = new GameObject("ChoiceButtonPrefab");
        btnPrefabGo.transform.SetParent(canvasGo.transform, false); // temporary parent
        btnPrefabGo.SetActive(false); // hide prefab
        
        var btnRect = btnPrefabGo.AddComponent<RectTransform>();
        btnRect.sizeDelta = new Vector2(560, 40);
        
        var btnImg = btnPrefabGo.AddComponent<Image>();
        btnImg.color = new Color(0.2f, 0.4f, 0.6f, 1f);
        
        var btn = btnPrefabGo.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color(0.2f, 0.4f, 0.6f, 1f);
        colors.highlightedColor = new Color(0.3f, 0.5f, 0.7f, 1f);
        colors.pressedColor = new Color(0.15f, 0.3f, 0.5f, 1f);
        btn.colors = colors;
        
        var btnTextGo = new GameObject("Text");
        btnTextGo.transform.SetParent(btnPrefabGo.transform, false);
        var btnTextRect = btnTextGo.AddComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;
        var btnTmp = btnTextGo.AddComponent<TextMeshProUGUI>();
        btnTmp.text = "Choice";
        btnTmp.fontSize = 18;
        btnTmp.alignment = TextAlignmentOptions.Center;
        btnTmp.color = Color.white;
        
        // Input field
        var inputGo = new GameObject("FreeTextInput");
        inputGo.transform.SetParent(layoutGo.transform, false);
        var inputRect = inputGo.AddComponent<RectTransform>();
        inputRect.sizeDelta = new Vector2(560, 35);
        var inputImg = inputGo.AddComponent<Image>();
        inputImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        var inputField = inputGo.AddComponent<TMP_InputField>();
        
        var inputTextGo = new GameObject("Text");
        inputTextGo.transform.SetParent(inputGo.transform, false);
        var inputTextRect = inputTextGo.AddComponent<RectTransform>();
        inputTextRect.anchorMin = Vector2.zero;
        inputTextRect.anchorMax = Vector2.one;
        inputTextRect.offsetMin = new Vector2(5, 2);
        inputTextRect.offsetMax = new Vector2(-5, -2);
        var inputTmp = inputTextGo.AddComponent<TextMeshProUGUI>();
        inputTmp.fontSize = 16;
        inputTmp.color = Color.white;
        inputField.textComponent = inputTmp;
        
        // Submit button
        var submitGo = new GameObject("SubmitButton");
        submitGo.transform.SetParent(layoutGo.transform, false);
        var submitRect = submitGo.AddComponent<RectTransform>();
        submitRect.sizeDelta = new Vector2(560, 35);
        var submitImg = submitGo.AddComponent<Image>();
        submitImg.color = new Color(0.2f, 0.6f, 0.3f, 1f);
        var submitBtn = submitGo.AddComponent<Button>();
        var submitTextGo = new GameObject("Text");
        submitTextGo.transform.SetParent(submitGo.transform, false);
        var submitTextRect = submitTextGo.AddComponent<RectTransform>();
        submitTextRect.anchorMin = Vector2.zero;
        submitTextRect.anchorMax = Vector2.one;
        submitTextRect.offsetMin = Vector2.zero;
        submitTextRect.offsetMax = Vector2.zero;
        var submitTmp = submitTextGo.AddComponent<TextMeshProUGUI>();
        submitTmp.text = "Kirim";
        submitTmp.fontSize = 18;
        submitTmp.alignment = TextAlignmentOptions.Center;
        submitTmp.color = Color.white;
        
        // Skip button
        var skipGo = new GameObject("SkipButton");
        skipGo.transform.SetParent(layoutGo.transform, false);
        var skipRect = skipGo.AddComponent<RectTransform>();
        skipRect.sizeDelta = new Vector2(560, 30);
        var skipImg = skipGo.AddComponent<Image>();
        skipImg.color = new Color(0.4f, 0.2f, 0.2f, 1f);
        var skipBtn = skipGo.AddComponent<Button>();
        var skipTextGo = new GameObject("Text");
        skipTextGo.transform.SetParent(skipGo.transform, false);
        var skipTextRect = skipTextGo.AddComponent<RectTransform>();
        skipTextRect.anchorMin = Vector2.zero;
        skipTextRect.anchorMax = Vector2.one;
        skipTextRect.offsetMin = Vector2.zero;
        skipTextRect.offsetMax = Vector2.zero;
        var skipTmp = skipTextGo.AddComponent<TextMeshProUGUI>();
        skipTmp.text = "Lewati Curhat";
        skipTmp.fontSize = 14;
        skipTmp.alignment = TextAlignmentOptions.Center;
        skipTmp.color = Color.white;
        
        // CurhatChoiceUI component
        var curhatUI = panelGo.AddComponent<CurhatChoiceUI>();
        curhatUI.panelRoot = panelGo;
        curhatUI.choiceButtonContainer = choiceContainer.transform;
        curhatUI.choiceButtonPrefab = btn;
        curhatUI.freeTextInput = inputField;
        curhatUI.submitButton = submitBtn;
        curhatUI.skipButton = skipBtn;
        
        Debug.Log("[MultiAgentSetup] Created CurhatChoiceUI with full layout.");
        return curhatUI;
    }
}
#endif
