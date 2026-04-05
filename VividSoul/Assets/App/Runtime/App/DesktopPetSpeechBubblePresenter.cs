#nullable enable

using System;
using UnityEngine;
using UnityEngine.UI;
using VividSoul.Runtime.Interaction;

namespace VividSoul.Runtime.App
{
    public sealed class DesktopPetSpeechBubblePresenter
    {
        private static readonly string[] BubbleFontCandidates =
        {
            "PingFang SC",
            "Hiragino Sans GB",
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "Arial Unicode MS",
            "Arial",
        };

        private const string BubbleBackgroundSpritePath = "Modern UI Pack/Textures/Border/Rounded/256px/Rounded Filled 256px";
        private const string BubbleShadowSpritePath = "Modern UI Pack/Textures/Shadow/Radial Shadow";
        private const string BubbleTailSpritePath = "Modern UI Pack/Textures/Border/Radial/32px/Radial Filled 32px";
        private const float BubbleMaxTextWidth = 312f;
        private const float BubbleMinBodyWidth = 176f;
        private const float BubbleMinBodyHeight = 88f;
        private const float BubbleBodyShadowWidthPadding = 36f;
        private const float BubbleBodyShadowHeightPadding = 30f;
        private const float BubbleBodyDepthOffsetY = -3f;
        private const float BubbleHighlightInset = 12f;
        private const float BubbleHighlightHeightRatio = 0.42f;
        private const float BubbleTailLargeSize = 18f;
        private const float BubbleTailSmallSize = 12f;
        private const float BubbleTailTinySize = 7f;
        private const float BubbleTailClusterHeight = 30f;
        private const float BubbleTailDepthOffsetX = 1.5f;
        private const float BubbleTailDepthOffsetY = -1.5f;
        private const float BubbleVerticalGap = 14f;
        private const float BubbleScreenPadding = 24f;
        private const float BubbleTextPaddingLeft = 26f;
        private const float BubbleTextPaddingRight = 26f;
        private const float BubbleTextPaddingTop = 24f;
        private const float BubbleTextPaddingBottom = 22f;
        private const float BubbleFadeDurationSeconds = 0.16f;
        private const float BubbleBaseHoldDurationSeconds = 1.8f;
        private const float BubblePerCharacterHoldDurationSeconds = 0.045f;
        private const float BubbleMinHoldDurationSeconds = 2.4f;
        private const float BubbleMaxHoldDurationSeconds = 5.8f;
        private const float BubbleCharactersPerSecond = 28f;
        private const int BubbleFontSize = 20;

        private static readonly Color BubbleFaceColor = new(1f, 0.985f, 0.992f, 0.996f);
        private static readonly Color BubbleDepthColor = new(0.95f, 0.82f, 0.90f, 0.98f);
        private static readonly Color BubbleStrokeColor = new(0.80f, 0.62f, 0.76f, 0.96f);
        private static readonly Color BubbleHighlightColor = new(1f, 1f, 1f, 0.72f);
        private static readonly Color BubbleShadowColor = new(0.32f, 0.18f, 0.28f, 0.16f);
        private static readonly Color BubbleTextColor = new(0.42f, 0.24f, 0.38f, 1f);

        private static Font? bubbleFont;
        private static Sprite? bubbleBackgroundSprite;
        private static Sprite? bubbleFallbackSprite;
        private static Sprite? bubbleShadowSprite;
        private static Sprite? bubbleTailSprite;

        private readonly DesktopPetBoundsService boundsService;

        private BubblePlaybackState playbackState;
        private BubbleUi? bubbleUi;
        private string currentMessage = string.Empty;
        private DesktopPetRuntimeController? runtimeController;
        private float remainingStateTime;
        private float visibleCharacterProgress;
        private int totalCharacterCount;

        public DesktopPetSpeechBubblePresenter(DesktopPetBoundsService boundsService)
        {
            this.boundsService = boundsService ?? throw new ArgumentNullException(nameof(boundsService));
        }

        public void Show(Canvas canvas, DesktopPetRuntimeController runtimeController, string message)
        {
            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            if (runtimeController == null)
            {
                throw new ArgumentNullException(nameof(runtimeController));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                HideImmediate();
                return;
            }

            EnsureBubbleUi(canvas);
            ResetBubbleState();
            this.runtimeController = runtimeController;
            currentMessage = message;
            totalCharacterCount = currentMessage.Length;
            visibleCharacterProgress = 0f;

            bubbleUi!.Root.gameObject.SetActive(true);
            bubbleUi.Root.SetAsFirstSibling();
            bubbleUi.CanvasGroup.alpha = 1f;
            bubbleUi.Text.text = string.Empty;
            ApplyBubbleLayout();

            if (totalCharacterCount <= 0)
            {
                playbackState = BubblePlaybackState.Holding;
                remainingStateTime = ComputeHoldDurationSeconds(0);
            }
            else
            {
                playbackState = BubblePlaybackState.Typing;
                remainingStateTime = 0f;
            }

            UpdatePosition();
        }

        public void Update(float deltaTime)
        {
            if (bubbleUi == null || playbackState == BubblePlaybackState.Hidden)
            {
                return;
            }

            if (!UpdatePosition())
            {
                HideImmediate();
                return;
            }

            switch (playbackState)
            {
                case BubblePlaybackState.Typing:
                    UpdateTyping(deltaTime);
                    break;
                case BubblePlaybackState.Holding:
                    UpdateHolding(deltaTime);
                    break;
                case BubblePlaybackState.Fading:
                    UpdateFading(deltaTime);
                    break;
            }
        }

        public void HideImmediate()
        {
            if (bubbleUi == null)
            {
                playbackState = BubblePlaybackState.Hidden;
                runtimeController = null;
                currentMessage = string.Empty;
                remainingStateTime = 0f;
                visibleCharacterProgress = 0f;
                totalCharacterCount = 0;
                return;
            }

            ResetBubbleState();
            bubbleUi.Root.gameObject.SetActive(false);
        }

        private void EnsureBubbleUi(Canvas canvas)
        {
            if (bubbleUi != null)
            {
                return;
            }

            var bubbleObject = new GameObject(
                "VividSoulSpeechBubble",
                typeof(RectTransform),
                typeof(CanvasGroup));
            var root = bubbleObject.GetComponent<RectTransform>();
            root.SetParent(canvas.transform, false);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0f);

            var canvasGroup = bubbleObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var bodyShadowObject = new GameObject(
                "BodyShadow",
                typeof(RectTransform),
                typeof(Image));
            var bodyShadow = bodyShadowObject.GetComponent<RectTransform>();
            bodyShadow.SetParent(root, false);
            bodyShadow.anchorMin = new Vector2(0.5f, 0f);
            bodyShadow.anchorMax = new Vector2(0.5f, 0f);
            bodyShadow.pivot = new Vector2(0.5f, 0f);

            var bodyShadowImage = bodyShadowObject.GetComponent<Image>();
            bodyShadowImage.sprite = GetBubbleShadowSprite();
            bodyShadowImage.type = Image.Type.Sliced;
            bodyShadowImage.color = BubbleShadowColor;
            bodyShadowImage.raycastTarget = false;

            var bodyDepthObject = new GameObject(
                "BodyDepth",
                typeof(RectTransform),
                typeof(Image));
            var bodyDepth = bodyDepthObject.GetComponent<RectTransform>();
            bodyDepth.SetParent(root, false);
            bodyDepth.anchorMin = new Vector2(0.5f, 0f);
            bodyDepth.anchorMax = new Vector2(0.5f, 0f);
            bodyDepth.pivot = new Vector2(0.5f, 0f);

            var bodyDepthImage = bodyDepthObject.GetComponent<Image>();
            bodyDepthImage.sprite = GetBubbleBackgroundSprite();
            bodyDepthImage.type = Image.Type.Sliced;
            bodyDepthImage.color = BubbleDepthColor;
            bodyDepthImage.raycastTarget = false;

            var bodyObject = new GameObject(
                "Body",
                typeof(RectTransform),
                typeof(Image),
                typeof(Mask));
            var body = bodyObject.GetComponent<RectTransform>();
            body.SetParent(root, false);
            body.anchorMin = new Vector2(0.5f, 0f);
            body.anchorMax = new Vector2(0.5f, 0f);
            body.pivot = new Vector2(0.5f, 0f);
            body.anchoredPosition = new Vector2(0f, BubbleTailClusterHeight);

            var bodyImage = bodyObject.GetComponent<Image>();
            bodyImage.sprite = GetBubbleBackgroundSprite();
            bodyImage.type = Image.Type.Sliced;
            bodyImage.color = BubbleFaceColor;
            bodyImage.raycastTarget = false;

            var bodyOutline = bodyObject.AddComponent<Outline>();
            bodyOutline.effectColor = BubbleStrokeColor;
            bodyOutline.effectDistance = new Vector2(1.4f, -1.4f);
            bodyOutline.useGraphicAlpha = true;

            var bodyMask = bodyObject.GetComponent<Mask>();
            bodyMask.showMaskGraphic = true;

            var highlightObject = new GameObject(
                "Highlight",
                typeof(RectTransform),
                typeof(Image));
            var highlight = highlightObject.GetComponent<RectTransform>();
            highlight.SetParent(body, false);
            highlight.anchorMin = new Vector2(0f, 1f);
            highlight.anchorMax = new Vector2(1f, 1f);
            highlight.pivot = new Vector2(0.5f, 1f);

            var highlightImage = highlightObject.GetComponent<Image>();
            highlightImage.sprite = GetBubbleBackgroundSprite();
            highlightImage.type = Image.Type.Sliced;
            highlightImage.color = BubbleHighlightColor;
            highlightImage.raycastTarget = false;

            var tailLargeDepthObject = new GameObject(
                "TailLargeDepth",
                typeof(RectTransform),
                typeof(Image));
            var tailLargeDepth = tailLargeDepthObject.GetComponent<RectTransform>();
            tailLargeDepth.SetParent(root, false);
            tailLargeDepth.anchorMin = new Vector2(0.5f, 0f);
            tailLargeDepth.anchorMax = new Vector2(0.5f, 0f);
            tailLargeDepth.pivot = new Vector2(0.5f, 0.5f);
            tailLargeDepth.sizeDelta = new Vector2(BubbleTailLargeSize, BubbleTailLargeSize);

            var tailLargeDepthImage = tailLargeDepthObject.GetComponent<Image>();
            tailLargeDepthImage.sprite = GetBubbleTailSprite();
            tailLargeDepthImage.type = Image.Type.Simple;
            tailLargeDepthImage.color = BubbleDepthColor;
            tailLargeDepthImage.raycastTarget = false;

            var tailLargeObject = new GameObject(
                "TailLarge",
                typeof(RectTransform),
                typeof(Image));
            var tailLarge = tailLargeObject.GetComponent<RectTransform>();
            tailLarge.SetParent(root, false);
            tailLarge.anchorMin = new Vector2(0.5f, 0f);
            tailLarge.anchorMax = new Vector2(0.5f, 0f);
            tailLarge.pivot = new Vector2(0.5f, 0.5f);
            tailLarge.sizeDelta = new Vector2(BubbleTailLargeSize, BubbleTailLargeSize);

            var tailLargeImage = tailLargeObject.GetComponent<Image>();
            tailLargeImage.sprite = GetBubbleTailSprite();
            tailLargeImage.type = Image.Type.Simple;
            tailLargeImage.color = BubbleFaceColor;
            tailLargeImage.raycastTarget = false;

            var tailLargeOutline = tailLargeObject.AddComponent<Outline>();
            tailLargeOutline.effectColor = BubbleStrokeColor;
            tailLargeOutline.effectDistance = new Vector2(1.2f, -1.2f);
            tailLargeOutline.useGraphicAlpha = true;

            var tailSmallDepthObject = new GameObject(
                "TailSmallDepth",
                typeof(RectTransform),
                typeof(Image));
            var tailSmallDepth = tailSmallDepthObject.GetComponent<RectTransform>();
            tailSmallDepth.SetParent(root, false);
            tailSmallDepth.anchorMin = new Vector2(0.5f, 0f);
            tailSmallDepth.anchorMax = new Vector2(0.5f, 0f);
            tailSmallDepth.pivot = new Vector2(0.5f, 0.5f);
            tailSmallDepth.sizeDelta = new Vector2(BubbleTailSmallSize, BubbleTailSmallSize);

            var tailSmallDepthImage = tailSmallDepthObject.GetComponent<Image>();
            tailSmallDepthImage.sprite = GetBubbleTailSprite();
            tailSmallDepthImage.type = Image.Type.Simple;
            tailSmallDepthImage.color = BubbleDepthColor;
            tailSmallDepthImage.raycastTarget = false;

            var tailSmallObject = new GameObject(
                "TailSmall",
                typeof(RectTransform),
                typeof(Image));
            var tailSmall = tailSmallObject.GetComponent<RectTransform>();
            tailSmall.SetParent(root, false);
            tailSmall.anchorMin = new Vector2(0.5f, 0f);
            tailSmall.anchorMax = new Vector2(0.5f, 0f);
            tailSmall.pivot = new Vector2(0.5f, 0.5f);
            tailSmall.sizeDelta = new Vector2(BubbleTailSmallSize, BubbleTailSmallSize);

            var tailSmallImage = tailSmallObject.GetComponent<Image>();
            tailSmallImage.sprite = GetBubbleTailSprite();
            tailSmallImage.type = Image.Type.Simple;
            tailSmallImage.color = BubbleFaceColor;
            tailSmallImage.raycastTarget = false;

            var tailSmallOutline = tailSmallObject.AddComponent<Outline>();
            tailSmallOutline.effectColor = BubbleStrokeColor;
            tailSmallOutline.effectDistance = new Vector2(1.2f, -1.2f);
            tailSmallOutline.useGraphicAlpha = true;

            var tailTinyDepthObject = new GameObject(
                "TailTinyDepth",
                typeof(RectTransform),
                typeof(Image));
            var tailTinyDepth = tailTinyDepthObject.GetComponent<RectTransform>();
            tailTinyDepth.SetParent(root, false);
            tailTinyDepth.anchorMin = new Vector2(0.5f, 0f);
            tailTinyDepth.anchorMax = new Vector2(0.5f, 0f);
            tailTinyDepth.pivot = new Vector2(0.5f, 0.5f);
            tailTinyDepth.sizeDelta = new Vector2(BubbleTailTinySize, BubbleTailTinySize);

            var tailTinyDepthImage = tailTinyDepthObject.GetComponent<Image>();
            tailTinyDepthImage.sprite = GetBubbleTailSprite();
            tailTinyDepthImage.type = Image.Type.Simple;
            tailTinyDepthImage.color = BubbleDepthColor;
            tailTinyDepthImage.raycastTarget = false;

            var tailTinyObject = new GameObject(
                "TailTiny",
                typeof(RectTransform),
                typeof(Image));
            var tailTiny = tailTinyObject.GetComponent<RectTransform>();
            tailTiny.SetParent(root, false);
            tailTiny.anchorMin = new Vector2(0.5f, 0f);
            tailTiny.anchorMax = new Vector2(0.5f, 0f);
            tailTiny.pivot = new Vector2(0.5f, 0.5f);
            tailTiny.sizeDelta = new Vector2(BubbleTailTinySize, BubbleTailTinySize);

            var tailTinyImage = tailTinyObject.GetComponent<Image>();
            tailTinyImage.sprite = GetBubbleTailSprite();
            tailTinyImage.type = Image.Type.Simple;
            tailTinyImage.color = BubbleFaceColor;
            tailTinyImage.raycastTarget = false;

            var tailTinyOutline = tailTinyObject.AddComponent<Outline>();
            tailTinyOutline.effectColor = BubbleStrokeColor;
            tailTinyOutline.effectDistance = new Vector2(1.1f, -1.1f);
            tailTinyOutline.useGraphicAlpha = true;

            var textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(Text));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(body, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(BubbleTextPaddingLeft, BubbleTextPaddingBottom);
            textRect.offsetMax = new Vector2(-BubbleTextPaddingRight, -BubbleTextPaddingTop);

            var text = textObject.GetComponent<Text>();
            text.font = GetBubbleFont();
            text.fontSize = BubbleFontSize;
            text.alignment = TextAnchor.UpperCenter;
            text.color = BubbleTextColor;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.12f;

            bubbleUi = new BubbleUi(
                root,
                canvasGroup,
                bodyShadow,
                bodyDepth,
                body,
                highlight,
                tailLargeDepth,
                tailLarge,
                tailSmallDepth,
                tailSmall,
                tailTinyDepth,
                tailTiny,
                textRect,
                text);
            root.gameObject.SetActive(false);
        }

        private void ApplyBubbleLayout()
        {
            var preferredSize = GetPreferredTextSize(currentMessage);
            var bodyWidth = Mathf.Clamp(
                preferredSize.x + BubbleTextPaddingLeft + BubbleTextPaddingRight,
                BubbleMinBodyWidth,
                BubbleMaxTextWidth + BubbleTextPaddingLeft + BubbleTextPaddingRight);
            var bodyHeight = Mathf.Max(
                BubbleMinBodyHeight,
                preferredSize.y + BubbleTextPaddingTop + BubbleTextPaddingBottom);
            var rootHeight = bodyHeight + BubbleTailClusterHeight;
            var tailBaseX = Mathf.Clamp(-bodyWidth * 0.08f, -26f, 6f);

            bubbleUi!.Body.sizeDelta = new Vector2(bodyWidth, bodyHeight);
            bubbleUi.BodyDepth.sizeDelta = new Vector2(bodyWidth, bodyHeight);
            bubbleUi.BodyDepth.anchoredPosition = new Vector2(0f, BubbleTailClusterHeight + BubbleBodyDepthOffsetY);
            bubbleUi.BodyShadow.sizeDelta = new Vector2(
                bodyWidth + BubbleBodyShadowWidthPadding,
                bodyHeight + BubbleBodyShadowHeightPadding);
            bubbleUi.BodyShadow.anchoredPosition = new Vector2(0f, BubbleTailClusterHeight - 10f);
            bubbleUi.Root.sizeDelta = new Vector2(bodyWidth, rootHeight);
            bubbleUi.Highlight.offsetMin = new Vector2(BubbleHighlightInset, -(bodyHeight * BubbleHighlightHeightRatio));
            bubbleUi.Highlight.offsetMax = new Vector2(-BubbleHighlightInset, -BubbleHighlightInset);
            bubbleUi.TailLargeDepth.anchoredPosition = new Vector2(
                tailBaseX + BubbleTailDepthOffsetX,
                BubbleTailClusterHeight - 7f + BubbleTailDepthOffsetY);
            bubbleUi.TailLarge.anchoredPosition = new Vector2(tailBaseX, BubbleTailClusterHeight - 7f);
            bubbleUi.TailSmallDepth.anchoredPosition = new Vector2(
                tailBaseX + 10f + BubbleTailDepthOffsetX,
                8f + BubbleTailDepthOffsetY);
            bubbleUi.TailSmall.anchoredPosition = new Vector2(tailBaseX + 10f, 8f);
            bubbleUi.TailTinyDepth.anchoredPosition = new Vector2(
                tailBaseX + 17f + BubbleTailDepthOffsetX,
                1f + BubbleTailDepthOffsetY);
            bubbleUi.TailTiny.anchoredPosition = new Vector2(tailBaseX + 17f, 1f);
            bubbleUi.TextRect.offsetMin = new Vector2(BubbleTextPaddingLeft, BubbleTextPaddingBottom);
            bubbleUi.TextRect.offsetMax = new Vector2(-BubbleTextPaddingRight, -BubbleTextPaddingTop);
        }

        private Vector2 GetPreferredTextSize(string text)
        {
            var settings = bubbleUi!.Text.GetGenerationSettings(new Vector2(BubbleMaxTextWidth, 0f));
            settings.generateOutOfBounds = true;
            settings.scaleFactor = 1f;
            settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;

            var preferredWidth = bubbleUi.Text.cachedTextGeneratorForLayout.GetPreferredWidth(text, settings) / bubbleUi.Text.pixelsPerUnit;
            var preferredHeight = bubbleUi.Text.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings) / bubbleUi.Text.pixelsPerUnit;
            return new Vector2(Mathf.Min(preferredWidth, BubbleMaxTextWidth), preferredHeight);
        }

        private bool UpdatePosition()
        {
            if (bubbleUi == null || runtimeController == null)
            {
                return false;
            }

            var currentModelRoot = runtimeController.CurrentModelRoot;
            var interactionCamera = runtimeController.InteractionCamera;
            if (currentModelRoot == null || interactionCamera == null)
            {
                return false;
            }

            if (!boundsService.TryGetScreenRect(interactionCamera, currentModelRoot, out var modelScreenRect))
            {
                return false;
            }

            var canvasRect = bubbleUi.Root.parent as RectTransform;
            if (canvasRect == null)
            {
                return false;
            }

            var bubbleScreenAnchor = new Vector2(modelScreenRect.center.x, modelScreenRect.yMax);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, bubbleScreenAnchor, null, out var bubbleLocalAnchor);

            var halfWidth = bubbleUi.Root.sizeDelta.x * 0.5f;
            var minX = canvasRect.rect.xMin + BubbleScreenPadding + halfWidth;
            var maxX = canvasRect.rect.xMax - BubbleScreenPadding - halfWidth;
            var minY = canvasRect.rect.yMin + BubbleScreenPadding;
            var maxY = canvasRect.rect.yMax - BubbleScreenPadding - bubbleUi.Root.sizeDelta.y;

            bubbleUi.Root.anchoredPosition = new Vector2(
                Mathf.Clamp(bubbleLocalAnchor.x, minX, maxX),
                Mathf.Clamp(bubbleLocalAnchor.y + BubbleVerticalGap, minY, maxY));
            return true;
        }

        private void UpdateTyping(float deltaTime)
        {
            visibleCharacterProgress += Mathf.Max(0f, deltaTime) * BubbleCharactersPerSecond;
            var visibleCharacters = Mathf.Clamp(Mathf.FloorToInt(visibleCharacterProgress), 0, totalCharacterCount);
            var nextVisibleText = visibleCharacters <= 0
                ? string.Empty
                : currentMessage.Substring(0, visibleCharacters);
            if (!string.Equals(bubbleUi!.Text.text, nextVisibleText, StringComparison.Ordinal))
            {
                bubbleUi.Text.text = nextVisibleText;
            }

            if (visibleCharacters < totalCharacterCount)
            {
                return;
            }

            playbackState = BubblePlaybackState.Holding;
            remainingStateTime = ComputeHoldDurationSeconds(totalCharacterCount);
        }

        private void UpdateHolding(float deltaTime)
        {
            remainingStateTime -= Mathf.Max(0f, deltaTime);
            if (remainingStateTime > 0f)
            {
                return;
            }

            playbackState = BubblePlaybackState.Fading;
            remainingStateTime = BubbleFadeDurationSeconds;
        }

        private void UpdateFading(float deltaTime)
        {
            remainingStateTime -= Mathf.Max(0f, deltaTime);
            var alpha = BubbleFadeDurationSeconds <= Mathf.Epsilon
                ? 0f
                : Mathf.Clamp01(remainingStateTime / BubbleFadeDurationSeconds);
            bubbleUi!.CanvasGroup.alpha = alpha;
            if (remainingStateTime > 0f)
            {
                return;
            }

            HideImmediate();
        }

        private void ResetBubbleState()
        {
            playbackState = BubblePlaybackState.Hidden;
            runtimeController = null;
            currentMessage = string.Empty;
            remainingStateTime = 0f;
            visibleCharacterProgress = 0f;
            totalCharacterCount = 0;

            if (bubbleUi == null)
            {
                return;
            }

            bubbleUi.CanvasGroup.alpha = 0f;
            bubbleUi.Text.text = string.Empty;
        }

        private static float ComputeHoldDurationSeconds(int characterCount)
        {
            return Mathf.Clamp(
                BubbleBaseHoldDurationSeconds + (characterCount * BubblePerCharacterHoldDurationSeconds),
                BubbleMinHoldDurationSeconds,
                BubbleMaxHoldDurationSeconds);
        }

        private static Font GetBubbleFont()
        {
            bubbleFont ??= Font.CreateDynamicFontFromOSFont(BubbleFontCandidates, BubbleFontSize);
            return bubbleFont != null
                ? bubbleFont
                : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static Sprite GetBubbleBackgroundSprite()
        {
            bubbleBackgroundSprite ??= Resources.Load<Sprite>(BubbleBackgroundSpritePath)
                                      ?? GetFallbackBubbleSprite();
            return bubbleBackgroundSprite;
        }

        private static Sprite GetBubbleShadowSprite()
        {
            bubbleShadowSprite ??= Resources.Load<Sprite>(BubbleShadowSpritePath)
                                  ?? GetFallbackBubbleSprite();
            return bubbleShadowSprite;
        }

        private static Sprite GetBubbleTailSprite()
        {
            bubbleTailSprite ??= Resources.Load<Sprite>(BubbleTailSpritePath)
                                ?? GetFallbackBubbleSprite();
            return bubbleTailSprite;
        }

        private static Sprite GetFallbackBubbleSprite()
        {
            bubbleFallbackSprite ??= Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            return bubbleFallbackSprite;
        }

        private sealed class BubbleUi
        {
            public BubbleUi(
                RectTransform root,
                CanvasGroup canvasGroup,
                RectTransform bodyShadow,
                RectTransform bodyDepth,
                RectTransform body,
                RectTransform highlight,
                RectTransform tailLargeDepth,
                RectTransform tailLarge,
                RectTransform tailSmallDepth,
                RectTransform tailSmall,
                RectTransform tailTinyDepth,
                RectTransform tailTiny,
                RectTransform textRect,
                Text text)
            {
                Root = root;
                CanvasGroup = canvasGroup;
                BodyShadow = bodyShadow;
                BodyDepth = bodyDepth;
                Body = body;
                Highlight = highlight;
                TailLargeDepth = tailLargeDepth;
                TailLarge = tailLarge;
                TailSmallDepth = tailSmallDepth;
                TailSmall = tailSmall;
                TailTinyDepth = tailTinyDepth;
                TailTiny = tailTiny;
                TextRect = textRect;
                Text = text;
            }

            public RectTransform Root { get; }

            public CanvasGroup CanvasGroup { get; }

            public RectTransform BodyShadow { get; }

            public RectTransform BodyDepth { get; }

            public RectTransform Body { get; }

            public RectTransform Highlight { get; }

            public RectTransform TailLargeDepth { get; }

            public RectTransform TailLarge { get; }

            public RectTransform TailSmallDepth { get; }

            public RectTransform TailSmall { get; }

            public RectTransform TailTinyDepth { get; }

            public RectTransform TailTiny { get; }

            public RectTransform TextRect { get; }

            public Text Text { get; }
        }

        private enum BubblePlaybackState
        {
            Hidden = 0,
            Typing = 1,
            Holding = 2,
            Fading = 3,
        }
    }
}
