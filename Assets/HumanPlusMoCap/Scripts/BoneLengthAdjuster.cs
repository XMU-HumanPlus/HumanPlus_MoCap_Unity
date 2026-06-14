using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HumanPlusMoCap.Scripts
{
    /// <summary>
    /// 运行时骨骼长度调整面板。
    /// </summary>
    public class BoneLengthAdjuster : MonoBehaviour
    {
        [Serializable]
        private class BoneDefinition
        {
            public string displayName;
            public HumanBodyBones bone;
            public Vector3 axisMultiplier = Vector3.one;
        }

        [Serializable]
        private class BoneFactorEntry
        {
            public string boneKey;
            public float factor = 1f;
        }

        [Serializable]
        private class BoneFactorFile
        {
            public string characterName;
            public List<BoneFactorEntry> bones = new List<BoneFactorEntry>();
        }

        private sealed class BoneRuntimeEntry
        {
            public BoneDefinition definition;
            public Transform boneTransform;
            public Transform baselineTransform;
            public Vector3 baselineScale;
            public float factor = 1f;
            public Text factorText;
            public InputField factorInput;
        }

        [Header("Target")]
        [SerializeField] private GameObject characterRoot;

        [Header("File")]
        [SerializeField] private string filePrefix = "BoneLengthAdjustments";

        [Header("Step")]
        [SerializeField] private float step = 0.05f;
        [SerializeField] private float minimumFactor = 0.05f;

        private readonly BoneDefinition[] boneDefinitions =
        {
            new BoneDefinition { displayName = "左大臂", bone = HumanBodyBones.LeftUpperArm, axisMultiplier = new Vector3(1f, 0f, 0f) },
            new BoneDefinition { displayName = "左小臂", bone = HumanBodyBones.LeftLowerArm, axisMultiplier = new Vector3(1f, 0f, 0f) },
            new BoneDefinition { displayName = "右大臂", bone = HumanBodyBones.RightUpperArm, axisMultiplier = new Vector3(1f, 0f, 0f) },
            new BoneDefinition { displayName = "右小臂", bone = HumanBodyBones.RightLowerArm, axisMultiplier = new Vector3(1f, 0f, 0f) },
            new BoneDefinition { displayName = "左大腿", bone = HumanBodyBones.LeftUpperLeg, axisMultiplier = new Vector3(0f, 1f, 0f) },
            new BoneDefinition { displayName = "左小腿", bone = HumanBodyBones.LeftLowerLeg, axisMultiplier = new Vector3(0f, 1f, 0f) },
            new BoneDefinition { displayName = "右大腿", bone = HumanBodyBones.RightUpperLeg, axisMultiplier = new Vector3(0f, 1f, 0f) },
            new BoneDefinition { displayName = "右小腿", bone = HumanBodyBones.RightLowerLeg, axisMultiplier = new Vector3(0f, 1f, 0f) },
        };

        private readonly List<BoneRuntimeEntry> runtimeEntries = new List<BoneRuntimeEntry>();

        private GameObject panelRoot;
        private Canvas panelCanvas;
        private CanvasGroup panelCanvasGroup;
        private ScrollRect scrollRect;
        private Transform contentRoot;
        private Text titleText;
        private Text tipText;
        private Button applyButton;
        private Button saveButton;
        private Button loadButton;
        private Button resetButton;

        private MoCapSrc activeMoCapSrc;
        private Animator activeAnimator;
        private Transform activeCharacterTransform;
        private float cachedGlobalWeight = 1f;
        private bool hasCachedGlobalWeight;
        private bool isOpen;
        private bool uiBuilt;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color InputColor = new Color(1f, 1f, 1f, 0.92f);

        private void Awake()
        {
            EnsurePanel();
            SetPanelVisible(false);
        }

        private void OnDestroy()
        {
            RestoreMocapWeight();
        }

        /// <summary>
        /// 打开骨骼长度调整面板。
        /// </summary>
        public void Open(GameObject rootOverride = null)
        {
            if (!ResolveTarget(rootOverride))
            {
                return;
            }

            cachedGlobalWeight = activeMoCapSrc.globalWeight;
            hasCachedGlobalWeight = true;
            activeMoCapSrc.globalWeight = 0f;

            BuildEntries();
            SnapshotBaseline();
            UpdateAllRows();
            ApplyPreviewToCharacter();

            isOpen = true;
            SetPanelVisible(true);
            UpdateHeader();
        }

        /// <summary>
        /// 关闭面板并恢复动捕全局权重。
        /// </summary>
        public void Close()
        {
            SetPanelVisible(false);
            isOpen = false;
            RestoreMocapWeight();
        }

        private bool ResolveTarget(GameObject rootOverride)
        {
            GameObject root = rootOverride != null ? rootOverride : characterRoot;
            if (root == null)
            {
                MoCapSrc discovered = FindObjectOfType<MoCapSrc>();
                if (discovered != null)
                {
                    root = discovered.gameObject;
                }
            }

            if (root == null)
            {
                Debug.LogError("[BoneLengthAdjuster] 未找到角色根节点。");
                return false;
            }

            MoCapSrc moCapSrc = root.GetComponent<MoCapSrc>();
            if (moCapSrc == null)
            {
                moCapSrc = root.GetComponentInChildren<MoCapSrc>(true);
            }

            if (moCapSrc == null)
            {
                Debug.LogError("[BoneLengthAdjuster] 指定角色根节点上未找到 MoCapSrc。");
                return false;
            }

            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                animator = root.GetComponentInChildren<Animator>(true);
            }

            if (animator == null)
            {
                Debug.LogError("[BoneLengthAdjuster] 指定角色根节点上未找到 Animator。");
                return false;
            }

            activeMoCapSrc = moCapSrc;
            activeAnimator = animator;
            activeCharacterTransform = root.transform;
            characterRoot = root;
            return true;
        }

        private void BuildEntries()
        {
            if (runtimeEntries.Count == 0)
            {
                foreach (BoneDefinition definition in boneDefinitions)
                {
                    BoneRuntimeEntry entry = new BoneRuntimeEntry
                    {
                        definition = definition,
                        boneTransform = activeAnimator.GetBoneTransform(definition.bone)
                    };

                    runtimeEntries.Add(entry);
                }
                return;
            }

            foreach (BoneRuntimeEntry entry in runtimeEntries)
            {
                entry.boneTransform = activeAnimator.GetBoneTransform(entry.definition.bone);
            }
        }

        private void SnapshotBaseline()
        {
            foreach (BoneRuntimeEntry entry in runtimeEntries)
            {
                if (entry.boneTransform == null)
                {
                    continue;
                }

                if (entry.baselineTransform != entry.boneTransform)
                {
                    entry.baselineTransform = entry.boneTransform;
                    entry.baselineScale = entry.boneTransform.localScale;
                }

                entry.factor = 1f;
            }
        }

        private void EnsurePanel()
        {
            if (uiBuilt)
            {
                return;
            }

            GameObject canvasObject = new GameObject("BoneLengthAdjusterCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            panelCanvas = canvasObject.GetComponent<Canvas>();
            panelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            panelCanvas.sortingOrder = short.MaxValue - 1;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            EventSystem eventSystem = FindObjectOfType<EventSystem>();
            if (eventSystem == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            panelRoot = new GameObject("BoneLengthAdjusterPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panelRoot.transform.SetParent(panelCanvas.transform, false);
            RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(860f, 600f);
            panelRect.anchoredPosition = Vector2.zero;

            Image panelImage = panelRoot.GetComponent<Image>();
            panelImage.color = PanelColor;

            panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;

            GameObject titleObject = CreateUIObject("Title", panelRoot.transform);
            titleText = CreateText(titleObject, "骨骼长度调整", 24, FontStyle.Bold, TextAnchor.MiddleCenter);
            StretchTop(titleObject.GetComponent<RectTransform>(), 20f, 20f, 20f, 40f);

            GameObject tipObject = CreateUIObject("Tip", panelRoot.transform);
            tipText = CreateText(tipObject, "滚动查看骨骼；输入比例或使用 +/- 调整。", 16, FontStyle.Normal, TextAnchor.MiddleLeft);
            StretchTop(tipObject.GetComponent<RectTransform>(), 20f, 70f, 20f, 28f);

            BuildScrollArea(panelRoot.transform, font);
            BuildFooter(panelRoot.transform, font);

            uiBuilt = true;
        }

        private void BuildScrollArea(Transform parent, Font font)
        {
            GameObject scrollObject = CreateUIObject("ScrollArea", parent);
            RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(20f, 100f);
            scrollRectTransform.offsetMax = new Vector2(-40f, -110f);

            Image background = scrollObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.06f);

            scrollRect = scrollObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            GameObject viewport = CreateUIObject("Viewport", scrollObject.transform);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-18f, 0f);

            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject content = CreateUIObject("Content", viewport.transform);
            contentRoot = content.transform;
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 10f;
            layout.padding = new RectOffset(8, 8, 8, 8);

            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            GameObject scrollbarObject = CreateUIObject("Scrollbar", scrollObject.transform);
            RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(14f, 0f);
            scrollbarRect.anchoredPosition = Vector2.zero;

            Image scrollbarBackground = scrollbarObject.AddComponent<Image>();
            scrollbarBackground.color = new Color(1f, 1f, 1f, 0.08f);

            Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            GameObject handle = CreateUIObject("Handle", scrollbarObject.transform);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(1f, 1f, 1f, 0.45f);
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handle.GetComponent<RectTransform>();

            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(1f, 1f);
            handleRect.offsetMax = new Vector2(-1f, -1f);

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        }

        private void BuildFooter(Transform parent, Font font)
        {
            GameObject footer = CreateUIObject("Footer", parent);
            RectTransform footerRect = footer.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.offsetMin = new Vector2(20f, 18f);
            footerRect.offsetMax = new Vector2(-20f, 86f);

            HorizontalLayoutGroup layout = footer.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 12f;

            applyButton = CreateButton(footer.transform, "Apply", "Apply");
            saveButton = CreateButton(footer.transform, "Save", "Save");
            loadButton = CreateButton(footer.transform, "Load", "Load");
            resetButton = CreateButton(footer.transform, "Reset", "Reset");

            applyButton.onClick.AddListener(ApplyCurrentChanges);
            saveButton.onClick.AddListener(SaveCurrentConfiguration);
            loadButton.onClick.AddListener(LoadConfigurationFromFile);
            resetButton.onClick.AddListener(ResetAllFactors);
        }

        private void BuildRow(BoneRuntimeEntry entry, Font font)
        {
            GameObject row = CreateUIObject(entry.definition.displayName + "Row", contentRoot);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0f, 46f);

            Image rowImage = row.AddComponent<Image>();
            rowImage.color = RowColor;

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            layout.spacing = 8f;
            layout.padding = new RectOffset(10, 10, 6, 6);

            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 46f;
            rowLayout.minHeight = 46f;

            GameObject nameObject = CreateUIObject("Name", row.transform);
            Text nameText = CreateText(nameObject, entry.definition.displayName, 18, FontStyle.Bold, TextAnchor.MiddleLeft);
            LayoutElement nameLayout = nameObject.AddComponent<LayoutElement>();
            nameLayout.minWidth = 150f;
            nameLayout.preferredWidth = 170f;

            GameObject valueObject = CreateUIObject("Value", row.transform);
            InputField inputField = CreateInputField(valueObject, font);
            LayoutElement valueLayout = valueObject.AddComponent<LayoutElement>();
            valueLayout.minWidth = 96f;
            valueLayout.preferredWidth = 112f;

            GameObject minusObject = CreateUIObject("Minus", row.transform);
            Button minusButton = CreateButton(minusObject.transform, "-", "-", 34f, 34f);
            LayoutElement minusLayout = minusObject.AddComponent<LayoutElement>();
            minusLayout.minWidth = 34f;
            minusLayout.preferredWidth = 34f;

            GameObject plusObject = CreateUIObject("Plus", row.transform);
            Button plusButton = CreateButton(plusObject.transform, "+", "+", 34f, 34f);
            LayoutElement plusLayout = plusObject.AddComponent<LayoutElement>();
            plusLayout.minWidth = 34f;
            plusLayout.preferredWidth = 34f;

            GameObject statusObject = CreateUIObject("Status", row.transform);
            Text status = CreateText(statusObject, entry.boneTransform == null ? "未找到骨骼" : " ", 15, FontStyle.Normal, TextAnchor.MiddleLeft);
            LayoutElement statusLayout = statusObject.AddComponent<LayoutElement>();
            statusLayout.minWidth = 160f;
            statusLayout.preferredWidth = 200f;

            entry.factorInput = inputField;
            entry.factorText = status;

            minusButton.onClick.AddListener(() => AdjustFactor(entry, -step));
            plusButton.onClick.AddListener(() => AdjustFactor(entry, step));
            inputField.onEndEdit.AddListener(value => HandleFactorEdited(entry, value));

            if (entry.boneTransform == null)
            {
                inputField.interactable = false;
                minusButton.interactable = false;
                plusButton.interactable = false;
            }

            RefreshRow(entry);
        }

        private void BuildEntriesIfNeeded()
        {
            if (contentRoot == null)
            {
                return;
            }

            if (contentRoot.childCount > 0)
            {
                return;
            }

            foreach (BoneRuntimeEntry entry in runtimeEntries)
            {
                BuildRow(entry, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            }
        }

        private void UpdateAllRows()
        {
            BuildEntriesIfNeeded();

            foreach (BoneRuntimeEntry entry in runtimeEntries)
            {
                RefreshRow(entry);
            }

            Canvas.ForceUpdateCanvases();
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private void RefreshRow(BoneRuntimeEntry entry)
        {
            if (entry.factorInput != null)
            {
                entry.factorInput.SetTextWithoutNotify(entry.factor.ToString("0.00", CultureInfo.InvariantCulture));
            }

            if (entry.factorText != null)
            {
                if (entry.boneTransform == null)
                {
                    entry.factorText.text = "未找到骨骼";
                }
                else
                {
                    entry.factorText.text = $"当前骨骼缩放基准：{FormatVector3(entry.baselineScale)}";
                }
            }
        }

        private void UpdateHeader()
        {
            if (titleText != null && activeCharacterTransform != null)
            {
                titleText.text = $"骨骼长度调整 - {activeCharacterTransform.name}";
            }

            if (tipText != null)
            {
                tipText.text = "滚动查看骨骼；输入比例或使用 +/- 调整。Apply 会正式应用并恢复动捕权重。";
            }
        }

        private void AdjustFactor(BoneRuntimeEntry entry, float delta)
        {
            SetFactor(entry, entry.factor + delta);
        }

        private void HandleFactorEdited(BoneRuntimeEntry entry, string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
                !float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                RefreshRow(entry);
                return;
            }

            SetFactor(entry, parsed);
        }

        private void SetFactor(BoneRuntimeEntry entry, float factor)
        {
            entry.factor = Mathf.Max(minimumFactor, factor);
            RefreshRow(entry);
            ApplyPreviewToCharacter();
        }

        private void ApplyPreviewToBone(BoneRuntimeEntry entry)
        {
            if (entry.boneTransform == null)
            {
                return;
            }

            Vector3 factorScale = Vector3.one;
            factorScale.x = Mathf.Approximately(entry.definition.axisMultiplier.x, 0f) ? 1f : entry.factor;
            factorScale.y = Mathf.Approximately(entry.definition.axisMultiplier.y, 0f) ? 1f : entry.factor;
            factorScale.z = Mathf.Approximately(entry.definition.axisMultiplier.z, 0f) ? 1f : entry.factor;
            entry.boneTransform.localScale = Vector3.Scale(entry.baselineScale, factorScale);
        }

        private void ApplyPreviewToCharacter()
        {
            foreach (BoneRuntimeEntry entry in runtimeEntries)
            {
                ApplyPreviewToBone(entry);
            }

            EnsureFeetWorldYZero();
        }

        private void ApplyCurrentChanges()
        {
            if (!isOpen)
            {
                return;
            }

            ApplyPreviewToCharacter();
            SetPanelVisible(false);
            isOpen = false;
            RestoreMocapWeight();
        }

        private void SaveCurrentConfiguration()
        {
            if (!ResolveFilePath(out string filePath))
            {
                return;
            }

            BoneFactorFile data = new BoneFactorFile
            {
                characterName = activeCharacterTransform != null ? activeCharacterTransform.name : string.Empty
            };

            foreach (BoneRuntimeEntry entry in runtimeEntries)
            {
                data.bones.Add(new BoneFactorEntry
                {
                    boneKey = entry.definition.bone.ToString(),
                    factor = entry.factor
                });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonUtility.ToJson(data, true));
            Debug.Log($"[BoneLengthAdjuster] 已保存骨骼配置: {filePath}");
        }

        private void LoadConfigurationFromFile()
        {
            if (!ResolveFilePath(out string filePath))
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                Debug.LogError($"[BoneLengthAdjuster] 配置文件不存在: {filePath}");
                return;
            }

            BoneFactorFile data = JsonUtility.FromJson<BoneFactorFile>(File.ReadAllText(filePath));
            if (data == null || data.bones == null)
            {
                Debug.LogError($"[BoneLengthAdjuster] 配置文件损坏: {filePath}");
                return;
            }

            Dictionary<string, float> factorMap = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (BoneFactorEntry boneEntry in data.bones)
            {
                if (!string.IsNullOrWhiteSpace(boneEntry.boneKey))
                {
                    factorMap[boneEntry.boneKey] = boneEntry.factor;
                }
            }

            foreach (BoneRuntimeEntry entry in runtimeEntries)
            {
                if (factorMap.TryGetValue(entry.definition.bone.ToString(), out float factor))
                {
                    entry.factor = Mathf.Max(minimumFactor, factor);
                }
            }

            UpdateAllRows();
            ApplyPreviewToCharacter();
            Debug.Log($"[BoneLengthAdjuster] 已载入骨骼配置: {filePath}");
        }

        private void ResetAllFactors()
        {
            foreach (BoneRuntimeEntry entry in runtimeEntries)
            {
                entry.factor = 1f;
            }

            UpdateAllRows();
            ApplyPreviewToCharacter();
        }

        private void EnsureFeetWorldYZero()
        {
            if (activeAnimator == null || activeCharacterTransform == null)
            {
                return;
            }

            Transform leftFoot = activeAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = activeAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (leftFoot == null && rightFoot == null)
            {
                return;
            }

            float totalY = 0f;
            int count = 0;
            if (leftFoot != null)
            {
                totalY += leftFoot.position.y;
                count++;
            }

            if (rightFoot != null)
            {
                totalY += rightFoot.position.y;
                count++;
            }

            if (count == 0)
            {
                return;
            }

            float offsetY = -(totalY / count);
            Vector3 rootPosition = activeCharacterTransform.position;
            activeCharacterTransform.position = new Vector3(rootPosition.x, rootPosition.y + offsetY, rootPosition.z);
        }

        private bool ResolveFilePath(out string filePath)
        {
            filePath = string.Empty;
            if (activeCharacterTransform == null)
            {
                Debug.LogError("[BoneLengthAdjuster] 角色未初始化。");
                return false;
            }

            string safeName = MakeSafeFileName(activeCharacterTransform.name);
            string directory = Path.Combine(Application.persistentDataPath, filePrefix);
            filePath = Path.Combine(directory, safeName + ".json");
            return true;
        }

        private void RestoreMocapWeight()
        {
            if (!hasCachedGlobalWeight || activeMoCapSrc == null)
            {
                return;
            }

            activeMoCapSrc.globalWeight = cachedGlobalWeight;
            hasCachedGlobalWeight = false;
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(visible);
            }
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Text CreateText(GameObject target, string text, int fontSize, FontStyle style, TextAnchor alignment)
        {
            Text uiText = target.AddComponent<Text>();
            uiText.text = text;
            uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            uiText.fontSize = fontSize;
            uiText.fontStyle = style;
            uiText.alignment = alignment;
            uiText.color = Color.white;
            uiText.raycastTarget = false;
            return uiText;
        }

        private static Button CreateButton(Transform parent, string name, string label, float width = 120f, float height = 42f)
        {
            GameObject buttonObject = CreateUIObject(name, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = ButtonColor;

            Button button = buttonObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.85f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = new Color(1f, 1f, 1f, 1f);
            button.colors = colors;

            GameObject textObject = CreateUIObject("Text", buttonObject.transform);
            Text buttonText = CreateText(textObject, label, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(width, height);
            LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.minWidth = width;
            layoutElement.preferredWidth = width;
            layoutElement.minHeight = height;
            layoutElement.preferredHeight = height;
            StretchFull(buttonText.rectTransform);
            return button;
        }

        private static InputField CreateInputField(GameObject target, Font font)
        {
            Image image = target.AddComponent<Image>();
            image.color = InputColor;

            InputField inputField = target.AddComponent<InputField>();
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.contentType = InputField.ContentType.DecimalNumber;

            GameObject textObject = CreateUIObject("Text", target.transform);
            Text text = CreateText(textObject, "1.00", 18, FontStyle.Bold, TextAnchor.MiddleCenter);
            text.color = Color.black;
            text.raycastTarget = false;
            inputField.textComponent = text;

            GameObject placeholderObject = CreateUIObject("Placeholder", target.transform);
            Text placeholder = CreateText(placeholderObject, "1.00", 16, FontStyle.Normal, TextAnchor.MiddleCenter);
            placeholder.color = new Color(0f, 0f, 0f, 0.35f);
            placeholder.raycastTarget = false;
            inputField.placeholder = placeholder;

            RectTransform rect = target.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(120f, 34f);
            StretchFull(text.rectTransform);
            StretchFull(placeholder.rectTransform);
            return inputField;
        }

        private static void StretchFull(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static void StretchTop(RectTransform rectTransform, float left, float top, float right, float height)
        {
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.offsetMin = new Vector2(left, -top - height);
            rectTransform.offsetMax = new Vector2(-right, -top);
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
        }

        private static string MakeSafeFileName(string fileName)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(fileName) ? "Character" : fileName;
        }
    }
}
