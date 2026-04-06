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

        private const string BubbleShadowSpritePath = "Modern UI Pack/Textures/Shadow/Radial Shadow";
        private const float BubbleMaxTextWidth = 440f;
        private const float BubbleMinBodyWidth = 228f;
        private const float BubbleMultiLineMinBodyWidth = 356f;
        private const float BubbleMinBodyHeight = 104f;
        private const float BubbleMaxBodyHeight = 336f;
        private const float BubbleBodyShadowWidthPadding = 40f;
        private const float BubbleBodyShadowHeightPadding = 34f;
        private const float BubbleBodyDepthOffsetY = -2f;
        private const float BubbleBodyStrokeExpansion = 5f;
        private const float BubbleHighlightInset = 15f;
        private const float BubbleHighlightHeightRatio = 0.36f;
        private const float BubbleTailLargeSize = 18f;
        private const float BubbleTailSmallSize = 12f;
        private const float BubbleTailTinySize = 7f;
        private const float BubbleTailLargeStrokeSize = 21f;
        private const float BubbleTailSmallStrokeSize = 14f;
        private const float BubbleTailTinyStrokeSize = 8.5f;
        private const float BubbleTailClusterHeight = 30f;
        private const float BubbleTailDepthOffsetX = 1.5f;
        private const float BubbleTailDepthOffsetY = -1.5f;
        private const float BubbleVerticalGap = 14f;
        private const float BubbleScreenPadding = 28f;
        private const float BubbleTextPaddingLeft = 36f;
        private const float BubbleTextPaddingRight = 38f;
        private const float BubbleTextPaddingTop = 30f;
        private const float BubbleTextPaddingBottom = 28f;
        private const float BubbleWrappedTextSidePaddingBonus = 8f;
        private const float BubbleWrappedTextTopPaddingBonus = 4f;
        private const float BubbleExtraTextHeightPadding = 14f;
        private const float BubbleExtraTextHeightPerLine = 5f;
        private const float BubbleShapeRasterScale = 4f;
        private const float BubbleFadeDurationSeconds = 0.16f;
        private const float BubbleBaseHoldDurationSeconds = 1.8f;
        private const float BubblePerCharacterHoldDurationSeconds = 0.045f;
        private const float BubbleMinHoldDurationSeconds = 2.4f;
        private const float BubbleMaxHoldDurationSeconds = 5.8f;
        private const float BubbleCharactersPerSecond = 28f;
        private const int BubbleFontSize = 19;

        private static readonly Color BubbleFaceColor = new(1f, 0.985f, 0.992f, 0.996f);
        private static readonly Color BubbleDepthColor = new(0.95f, 0.82f, 0.90f, 0.98f);
        private static readonly Color BubbleStrokeColor = new(0.90f, 0.78f, 0.86f, 0.84f);
        private static readonly Color BubbleHighlightColor = new(1f, 1f, 1f, 0.72f);
        private static readonly Color BubbleShadowColor = new(0.32f, 0.18f, 0.28f, 0.16f);
        private static readonly Color BubbleTextColor = new(0.42f, 0.24f, 0.38f, 1f);

        private static Font? bubbleFont;
        private static Sprite? bubbleFallbackSprite;
        private static Sprite? bubbleShadowSprite;

        private readonly DesktopPetBoundsService boundsService;

        private BubblePlaybackState playbackState;
        private BubbleUi? bubbleUi;
        private string currentMessage = string.Empty;
        private DesktopPetRuntimeController? runtimeController;
        private float remainingStateTime;
        private float visibleCharacterProgress;
        private int totalCharacterCount;
        private bool bubbleRequiresVerticalScroll;

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
            ApplyBubbleLayout();
            UpdateVisibleText(string.Empty, false);

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
            bodyDepthImage.sprite = GetFallbackBubbleSprite();
            bodyDepthImage.type = Image.Type.Simple;
            bodyDepthImage.color = BubbleDepthColor;
            bodyDepthImage.raycastTarget = false;

            var bodyStrokeObject = new GameObject(
                "BodyStroke",
                typeof(RectTransform),
                typeof(Image));
            var bodyStroke = bodyStrokeObject.GetComponent<RectTransform>();
            bodyStroke.SetParent(root, false);
            bodyStroke.anchorMin = new Vector2(0.5f, 0f);
            bodyStroke.anchorMax = new Vector2(0.5f, 0f);
            bodyStroke.pivot = new Vector2(0.5f, 0f);
            bodyStroke.anchoredPosition = new Vector2(0f, BubbleTailClusterHeight);

            var bodyStrokeImage = bodyStrokeObject.GetComponent<Image>();
            bodyStrokeImage.sprite = GetFallbackBubbleSprite();
            bodyStrokeImage.type = Image.Type.Simple;
            bodyStrokeImage.color = BubbleStrokeColor;
            bodyStrokeImage.raycastTarget = false;

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
            bodyImage.sprite = GetFallbackBubbleSprite();
            bodyImage.type = Image.Type.Simple;
            bodyImage.color = BubbleFaceColor;
            bodyImage.raycastTarget = true;

            var bodyMask = bodyObject.GetComponent<Mask>();
            bodyMask.showMaskGraphic = true;

            var bodyScrollRect = bodyObject.AddComponent<ScrollRect>();
            bodyScrollRect.horizontal = false;
            bodyScrollRect.vertical = true;
            bodyScrollRect.movementType = ScrollRect.MovementType.Clamped;
            bodyScrollRect.inertia = false;
            bodyScrollRect.scrollSensitivity = 28f;

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
            highlightImage.sprite = GetFallbackBubbleSprite();
            highlightImage.type = Image.Type.Simple;
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
            tailLargeDepthImage.sprite = GetFallbackBubbleSprite();
            tailLargeDepthImage.type = Image.Type.Simple;
            tailLargeDepthImage.color = BubbleDepthColor;
            tailLargeDepthImage.raycastTarget = false;

            var tailLargeStrokeObject = new GameObject(
                "TailLargeStroke",
                typeof(RectTransform),
                typeof(Image));
            var tailLargeStroke = tailLargeStrokeObject.GetComponent<RectTransform>();
            tailLargeStroke.SetParent(root, false);
            tailLargeStroke.anchorMin = new Vector2(0.5f, 0f);
            tailLargeStroke.anchorMax = new Vector2(0.5f, 0f);
            tailLargeStroke.pivot = new Vector2(0.5f, 0.5f);
            tailLargeStroke.sizeDelta = new Vector2(BubbleTailLargeStrokeSize, BubbleTailLargeStrokeSize);

            var tailLargeStrokeImage = tailLargeStrokeObject.GetComponent<Image>();
            tailLargeStrokeImage.sprite = GetFallbackBubbleSprite();
            tailLargeStrokeImage.type = Image.Type.Simple;
            tailLargeStrokeImage.color = BubbleStrokeColor;
            tailLargeStrokeImage.raycastTarget = false;

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
            tailLargeImage.sprite = GetFallbackBubbleSprite();
            tailLargeImage.type = Image.Type.Simple;
            tailLargeImage.color = BubbleFaceColor;
            tailLargeImage.raycastTarget = false;

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
            tailSmallDepthImage.sprite = GetFallbackBubbleSprite();
            tailSmallDepthImage.type = Image.Type.Simple;
            tailSmallDepthImage.color = BubbleDepthColor;
            tailSmallDepthImage.raycastTarget = false;

            var tailSmallStrokeObject = new GameObject(
                "TailSmallStroke",
                typeof(RectTransform),
                typeof(Image));
            var tailSmallStroke = tailSmallStrokeObject.GetComponent<RectTransform>();
            tailSmallStroke.SetParent(root, false);
            tailSmallStroke.anchorMin = new Vector2(0.5f, 0f);
            tailSmallStroke.anchorMax = new Vector2(0.5f, 0f);
            tailSmallStroke.pivot = new Vector2(0.5f, 0.5f);
            tailSmallStroke.sizeDelta = new Vector2(BubbleTailSmallStrokeSize, BubbleTailSmallStrokeSize);

            var tailSmallStrokeImage = tailSmallStrokeObject.GetComponent<Image>();
            tailSmallStrokeImage.sprite = GetFallbackBubbleSprite();
            tailSmallStrokeImage.type = Image.Type.Simple;
            tailSmallStrokeImage.color = BubbleStrokeColor;
            tailSmallStrokeImage.raycastTarget = false;

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
            tailSmallImage.sprite = GetFallbackBubbleSprite();
            tailSmallImage.type = Image.Type.Simple;
            tailSmallImage.color = BubbleFaceColor;
            tailSmallImage.raycastTarget = false;

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
            tailTinyDepthImage.sprite = GetFallbackBubbleSprite();
            tailTinyDepthImage.type = Image.Type.Simple;
            tailTinyDepthImage.color = BubbleDepthColor;
            tailTinyDepthImage.raycastTarget = false;

            var tailTinyStrokeObject = new GameObject(
                "TailTinyStroke",
                typeof(RectTransform),
                typeof(Image));
            var tailTinyStroke = tailTinyStrokeObject.GetComponent<RectTransform>();
            tailTinyStroke.SetParent(root, false);
            tailTinyStroke.anchorMin = new Vector2(0.5f, 0f);
            tailTinyStroke.anchorMax = new Vector2(0.5f, 0f);
            tailTinyStroke.pivot = new Vector2(0.5f, 0.5f);
            tailTinyStroke.sizeDelta = new Vector2(BubbleTailTinyStrokeSize, BubbleTailTinyStrokeSize);

            var tailTinyStrokeImage = tailTinyStrokeObject.GetComponent<Image>();
            tailTinyStrokeImage.sprite = GetFallbackBubbleSprite();
            tailTinyStrokeImage.type = Image.Type.Simple;
            tailTinyStrokeImage.color = BubbleStrokeColor;
            tailTinyStrokeImage.raycastTarget = false;

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
            tailTinyImage.sprite = GetFallbackBubbleSprite();
            tailTinyImage.type = Image.Type.Simple;
            tailTinyImage.color = BubbleFaceColor;
            tailTinyImage.raycastTarget = false;

            var textContentObject = new GameObject(
                "TextContent",
                typeof(RectTransform));
            var textContentRect = textContentObject.GetComponent<RectTransform>();
            textContentRect.SetParent(body, false);
            textContentRect.anchorMin = new Vector2(0f, 1f);
            textContentRect.anchorMax = new Vector2(1f, 1f);
            textContentRect.pivot = new Vector2(0.5f, 1f);

            var textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(Text));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(textContentRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.font = GetBubbleFont();
            text.fontSize = BubbleFontSize;
            text.alignment = TextAnchor.UpperLeft;
            text.color = BubbleTextColor;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.18f;

            bodyScrollRect.viewport = body;
            bodyScrollRect.content = textContentRect;
            bodyScrollRect.verticalNormalizedPosition = 1f;

            bubbleUi = new BubbleUi(
                root,
                canvasGroup,
                bodyShadow,
                bodyDepth,
                bodyStroke,
                body,
                bodyImage,
                bodyScrollRect,
                highlight,
                tailLargeDepth,
                tailLargeStroke,
                tailLarge,
                tailSmallDepth,
                tailSmallStroke,
                tailSmall,
                tailTinyDepth,
                tailTinyStroke,
                tailTiny,
                textContentRect,
                textRect,
                text);
            root.gameObject.SetActive(false);
        }

        private void ApplyBubbleLayout()
        {
            var (preferredSize, lineCount) = GetPreferredTextMetrics(currentMessage);
            var hasWrappedText = lineCount > 1;
            var minBodyWidth = hasWrappedText ? BubbleMultiLineMinBodyWidth : BubbleMinBodyWidth;
            var textPaddingLeft = BubbleTextPaddingLeft + (hasWrappedText ? BubbleWrappedTextSidePaddingBonus : 0f);
            var textPaddingRight = BubbleTextPaddingRight + (hasWrappedText ? BubbleWrappedTextSidePaddingBonus : 0f);
            var textPaddingTop = BubbleTextPaddingTop + (hasWrappedText ? BubbleWrappedTextTopPaddingBonus : 0f);
            var textPaddingBottom = BubbleTextPaddingBottom;
            var canvasRect = bubbleUi!.Root.parent as RectTransform;
            var maxBodyHeight = GetMaxBodyHeight(canvasRect);
            var bodyWidth = Mathf.Clamp(
                preferredSize.x + textPaddingLeft + textPaddingRight,
                minBodyWidth,
                BubbleMaxTextWidth + textPaddingLeft + textPaddingRight);
            var preferredBodyHeight = Mathf.Max(
                BubbleMinBodyHeight,
                preferredSize.y + textPaddingTop + textPaddingBottom);
            var bodyHeight = Mathf.Min(preferredBodyHeight, maxBodyHeight);
            bubbleRequiresVerticalScroll = preferredBodyHeight > bodyHeight + 0.5f;
            var rootHeight = bodyHeight + BubbleTailClusterHeight;
            var tailBaseX = Mathf.Clamp(-bodyWidth * 0.08f, -26f, 6f);

            bubbleUi.Body.sizeDelta = new Vector2(bodyWidth, bodyHeight);
            bubbleUi.BodyDepth.sizeDelta = new Vector2(bodyWidth, bodyHeight);
            bubbleUi.BodyDepth.anchoredPosition = new Vector2(0f, BubbleTailClusterHeight + BubbleBodyDepthOffsetY);
            bubbleUi.BodyStroke.sizeDelta = new Vector2(
                bodyWidth + BubbleBodyStrokeExpansion,
                bodyHeight + BubbleBodyStrokeExpansion);
            bubbleUi.BodyStroke.anchoredPosition = new Vector2(0f, BubbleTailClusterHeight - (BubbleBodyStrokeExpansion * 0.5f));
            UpdateBubbleVectorSprites(bodyWidth, bodyHeight);
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
            bubbleUi.TailLargeStroke.anchoredPosition = new Vector2(tailBaseX, BubbleTailClusterHeight - 7f);
            bubbleUi.TailLarge.anchoredPosition = new Vector2(tailBaseX, BubbleTailClusterHeight - 7f);
            bubbleUi.TailSmallDepth.anchoredPosition = new Vector2(
                tailBaseX + 10f + BubbleTailDepthOffsetX,
                8f + BubbleTailDepthOffsetY);
            bubbleUi.TailSmallStroke.anchoredPosition = new Vector2(tailBaseX + 10f, 8f);
            bubbleUi.TailSmall.anchoredPosition = new Vector2(tailBaseX + 10f, 8f);
            bubbleUi.TailTinyDepth.anchoredPosition = new Vector2(
                tailBaseX + 17f + BubbleTailDepthOffsetX,
                1f + BubbleTailDepthOffsetY);
            bubbleUi.TailTinyStroke.anchoredPosition = new Vector2(tailBaseX + 17f, 1f);
            bubbleUi.TailTiny.anchoredPosition = new Vector2(tailBaseX + 17f, 1f);
            if (bubbleRequiresVerticalScroll)
            {
                bubbleUi.Text.alignment = TextAnchor.UpperLeft;
                bubbleUi.TextContentRect.anchorMin = new Vector2(0f, 1f);
                bubbleUi.TextContentRect.anchorMax = new Vector2(1f, 1f);
                bubbleUi.TextContentRect.pivot = new Vector2(0.5f, 1f);
                bubbleUi.TextContentRect.offsetMin = new Vector2(textPaddingLeft, -(preferredSize.y + textPaddingTop));
                bubbleUi.TextContentRect.offsetMax = new Vector2(-textPaddingRight, -textPaddingTop);
            }
            else
            {
                bubbleUi.Text.alignment = TextAnchor.MiddleLeft;
                bubbleUi.TextContentRect.anchorMin = Vector2.zero;
                bubbleUi.TextContentRect.anchorMax = Vector2.one;
                bubbleUi.TextContentRect.pivot = new Vector2(0.5f, 0.5f);
                bubbleUi.TextContentRect.offsetMin = new Vector2(textPaddingLeft, textPaddingBottom);
                bubbleUi.TextContentRect.offsetMax = new Vector2(-textPaddingRight, -textPaddingTop);
            }

            bubbleUi.TextRect.offsetMin = Vector2.zero;
            bubbleUi.TextRect.offsetMax = Vector2.zero;
            bubbleUi.BodyImage.raycastTarget = bubbleRequiresVerticalScroll;
            bubbleUi.ScrollRect.enabled = bubbleRequiresVerticalScroll;
            bubbleUi.ScrollRect.vertical = bubbleRequiresVerticalScroll;
            bubbleUi.CanvasGroup.interactable = bubbleRequiresVerticalScroll;
            bubbleUi.CanvasGroup.blocksRaycasts = bubbleRequiresVerticalScroll;
            ApplyScrollPosition(bubbleRequiresVerticalScroll ? 0f : 1f);
        }

        private (Vector2 Size, int LineCount) GetPreferredTextMetrics(string text)
        {
            var settings = bubbleUi!.Text.GetGenerationSettings(new Vector2(BubbleMaxTextWidth, 0f));
            settings.generateOutOfBounds = true;
            settings.scaleFactor = 1f;
            settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.richText = bubbleUi.Text.supportRichText;

            var generator = new TextGenerator(text.Length + 32);
            generator.Populate(text, settings);
            var preferredWidth = bubbleUi.Text.cachedTextGeneratorForLayout.GetPreferredWidth(text, settings) / bubbleUi.Text.pixelsPerUnit;
            var preferredHeight = bubbleUi.Text.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings) / bubbleUi.Text.pixelsPerUnit;
            var lineCount = Mathf.Max(1, generator.lineCount);
            var adjustedHeight = preferredHeight
                                 + BubbleExtraTextHeightPadding
                                 + Mathf.Max(0, lineCount - 1) * BubbleExtraTextHeightPerLine;
            return (new Vector2(Mathf.Min(preferredWidth, BubbleMaxTextWidth), adjustedHeight), lineCount);
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
                UpdateVisibleText(nextVisibleText, bubbleRequiresVerticalScroll);
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
            bubbleRequiresVerticalScroll = false;

            if (bubbleUi == null)
            {
                return;
            }

            bubbleUi.CanvasGroup.alpha = 0f;
            bubbleUi.CanvasGroup.interactable = false;
            bubbleUi.CanvasGroup.blocksRaycasts = false;
            bubbleUi.BodyImage.raycastTarget = false;
            bubbleUi.ScrollRect.enabled = false;
            bubbleUi.ScrollRect.vertical = false;
            bubbleUi.TextContentRect.offsetMin = new Vector2(BubbleTextPaddingLeft, -BubbleTextPaddingTop);
            bubbleUi.TextContentRect.offsetMax = new Vector2(-BubbleTextPaddingRight, -BubbleTextPaddingTop);
            UpdateVisibleText(string.Empty, false);
        }

        private static float ComputeHoldDurationSeconds(int characterCount)
        {
            return Mathf.Clamp(
                BubbleBaseHoldDurationSeconds + (characterCount * BubblePerCharacterHoldDurationSeconds),
                BubbleMinHoldDurationSeconds,
                BubbleMaxHoldDurationSeconds);
        }

        private void UpdateVisibleText(string text, bool stickToBottom)
        {
            if (bubbleUi == null)
            {
                return;
            }

            bubbleUi.Text.text = text;

            if (!bubbleRequiresVerticalScroll)
            {
                ApplyScrollPosition(1f);
                return;
            }

            var contentHeight = GetVisibleTextHeight(text);
            var contentTop = -bubbleUi.TextContentRect.offsetMax.y;
            bubbleUi.TextContentRect.offsetMin = new Vector2(
                bubbleUi.TextContentRect.offsetMin.x,
                -(contentHeight + contentTop));

            ApplyScrollPosition(stickToBottom ? 0f : 1f);
        }

        private float GetVisibleTextHeight(string text)
        {
            if (bubbleUi == null)
            {
                return 0f;
            }

            var availableWidth = Mathf.Max(
                1f,
                bubbleUi.Body.rect.width - bubbleUi.TextContentRect.offsetMin.x + bubbleUi.TextContentRect.offsetMax.x);
            var settings = bubbleUi.Text.GetGenerationSettings(new Vector2(availableWidth, 0f));
            settings.generateOutOfBounds = true;
            settings.scaleFactor = 1f;
            settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.richText = bubbleUi.Text.supportRichText;

            var generator = new TextGenerator((text?.Length ?? 0) + 32);
            generator.Populate(text, settings);
            var preferredHeight = bubbleUi.Text.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings) / bubbleUi.Text.pixelsPerUnit;
            var lineCount = Mathf.Max(1, generator.lineCount);
            return preferredHeight
                   + BubbleExtraTextHeightPadding
                   + Mathf.Max(0, lineCount - 1) * BubbleExtraTextHeightPerLine;
        }

        private void ApplyScrollPosition(float verticalNormalizedPosition)
        {
            if (bubbleUi == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            bubbleUi.ScrollRect.StopMovement();
            bubbleUi.ScrollRect.verticalNormalizedPosition = verticalNormalizedPosition;
        }

        private static float GetMaxBodyHeight(RectTransform? canvasRect)
        {
            if (canvasRect == null)
            {
                return BubbleMaxBodyHeight;
            }

            var availableHeight = canvasRect.rect.height - (BubbleScreenPadding * 2f) - BubbleTailClusterHeight - BubbleVerticalGap;
            return Mathf.Clamp(availableHeight, BubbleMinBodyHeight, BubbleMaxBodyHeight);
        }

        private void UpdateBubbleVectorSprites(float bodyWidth, float bodyHeight)
        {
            if (bubbleUi == null)
            {
                return;
            }

            ReplaceGeneratedSprite(
                bubbleUi.BodyDepth.GetComponent<Image>(),
                BuildRoundedRectSprite(bodyWidth, bodyHeight, BubbleDepthColor, null, 0f));
            ReplaceGeneratedSprite(
                bubbleUi.BodyStroke.GetComponent<Image>(),
                BuildRoundedRectSprite(
                    bodyWidth + BubbleBodyStrokeExpansion,
                    bodyHeight + BubbleBodyStrokeExpansion,
                    BubbleFaceColor,
                    BubbleStrokeColor,
                    0f));
            ReplaceGeneratedSprite(
                bubbleUi.BodyImage,
                BuildRoundedRectSprite(bodyWidth, bodyHeight, BubbleFaceColor, null, 0f));

            ReplaceGeneratedSprite(
                bubbleUi.TailLargeDepth.GetComponent<Image>(),
                BuildCircleSprite(BubbleTailLargeSize, BubbleDepthColor, null, 0f));
            ReplaceGeneratedSprite(
                bubbleUi.TailLargeStroke.GetComponent<Image>(),
                BuildCircleSprite(BubbleTailLargeStrokeSize, BubbleFaceColor, BubbleStrokeColor, 0f));
            ReplaceGeneratedSprite(
                bubbleUi.TailLarge.GetComponent<Image>(),
                BuildCircleSprite(BubbleTailLargeSize, BubbleFaceColor, null, 0f));

            ReplaceGeneratedSprite(
                bubbleUi.TailSmallDepth.GetComponent<Image>(),
                BuildCircleSprite(BubbleTailSmallSize, BubbleDepthColor, null, 0f));
            ReplaceGeneratedSprite(
                bubbleUi.TailSmallStroke.GetComponent<Image>(),
                BuildCircleSprite(BubbleTailSmallStrokeSize, BubbleFaceColor, BubbleStrokeColor, 0f));
            ReplaceGeneratedSprite(
                bubbleUi.TailSmall.GetComponent<Image>(),
                BuildCircleSprite(BubbleTailSmallSize, BubbleFaceColor, null, 0f));

            ReplaceGeneratedSprite(
                bubbleUi.TailTinyDepth.GetComponent<Image>(),
                BuildCircleSprite(BubbleTailTinySize, BubbleDepthColor, null, 0f));
            ReplaceGeneratedSprite(
                bubbleUi.TailTinyStroke.GetComponent<Image>(),
                BuildCircleSprite(BubbleTailTinyStrokeSize, BubbleFaceColor, BubbleStrokeColor, 0f));
            ReplaceGeneratedSprite(
                bubbleUi.TailTiny.GetComponent<Image>(),
                BuildCircleSprite(BubbleTailTinySize, BubbleFaceColor, null, 0f));
        }

        private static void ReplaceGeneratedSprite(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            var previousSprite = image.sprite;
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;

            if (previousSprite != null
                && previousSprite != bubbleFallbackSprite
                && previousSprite != bubbleShadowSprite)
            {
                var previousTexture = previousSprite.texture;
                if (previousTexture != null && previousTexture != Texture2D.whiteTexture)
                {
                    UnityEngine.Object.Destroy(previousTexture);
                }

                UnityEngine.Object.Destroy(previousSprite);
            }
        }

        private static Sprite BuildRoundedRectSprite(
            float width,
            float height,
            Color fillColor,
            Color? strokeColor,
            float strokeWidth)
        {
            var safeWidth = Mathf.Max(1f, width);
            var safeHeight = Mathf.Max(1f, height);
            var cornerRadius = Mathf.Clamp(safeHeight * 0.24f, 18f, safeHeight * 0.5f);
            return BuildRoundedRectMaskSprite(safeWidth, safeHeight, cornerRadius);
        }

        private static Sprite BuildCircleSprite(
            float size,
            Color fillColor,
            Color? strokeColor,
            float strokeWidth)
        {
            var safeSize = Mathf.Max(1f, size);
            return BuildCircleMaskSprite(safeSize);
        }

        private static Sprite BuildRoundedRectMaskSprite(float width, float height, float cornerRadius)
        {
            var textureWidth = GetShapeRasterDimension(width);
            var textureHeight = GetShapeRasterDimension(height);
            var texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[textureWidth * textureHeight];
            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            var radius = Mathf.Clamp(cornerRadius, 1f, Mathf.Min(halfWidth, halfHeight));
            var aaWidth = 1f / BubbleShapeRasterScale;

            for (var y = 0; y < textureHeight; y++)
            {
                var localY = ((y + 0.5f) / BubbleShapeRasterScale) - halfHeight;
                for (var x = 0; x < textureWidth; x++)
                {
                    var localX = ((x + 0.5f) / BubbleShapeRasterScale) - halfWidth;
                    var distance = SignedDistanceToRoundedRect(localX, localY, halfWidth, halfHeight, radius);
                    var alpha = Mathf.Clamp01(0.5f - (distance / aaWidth));
                    pixels[(y * textureWidth) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                BubbleShapeRasterScale);
        }

        private static Sprite BuildCircleMaskSprite(float size)
        {
            var textureSize = GetShapeRasterDimension(size);
            var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[textureSize * textureSize];
            var radius = size * 0.5f;
            var aaWidth = 1f / BubbleShapeRasterScale;

            for (var y = 0; y < textureSize; y++)
            {
                var localY = ((y + 0.5f) / BubbleShapeRasterScale) - radius;
                for (var x = 0; x < textureSize; x++)
                {
                    var localX = ((x + 0.5f) / BubbleShapeRasterScale) - radius;
                    var distance = Mathf.Sqrt((localX * localX) + (localY * localY)) - (radius - (aaWidth * 0.5f));
                    var alpha = Mathf.Clamp01(0.5f - (distance / aaWidth));
                    pixels[(y * textureSize) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                BubbleShapeRasterScale);
        }

        private static int GetShapeRasterDimension(float size)
        {
            return Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(1f, size) * BubbleShapeRasterScale),
                32,
                2048);
        }

        private static float SignedDistanceToRoundedRect(float x, float y, float halfWidth, float halfHeight, float radius)
        {
            var innerHalfWidth = Mathf.Max(0f, halfWidth - radius);
            var innerHalfHeight = Mathf.Max(0f, halfHeight - radius);
            var dx = Mathf.Abs(x) - innerHalfWidth;
            var dy = Mathf.Abs(y) - innerHalfHeight;
            var outsideX = Mathf.Max(dx, 0f);
            var outsideY = Mathf.Max(dy, 0f);
            var outsideDistance = Mathf.Sqrt((outsideX * outsideX) + (outsideY * outsideY));
            var insideDistance = Mathf.Min(Mathf.Max(dx, dy), 0f);
            return outsideDistance + insideDistance - radius;
        }

        private static Font GetBubbleFont()
        {
            bubbleFont ??= Font.CreateDynamicFontFromOSFont(BubbleFontCandidates, BubbleFontSize);
            return bubbleFont != null
                ? bubbleFont
                : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static Sprite GetBubbleShadowSprite()
        {
            bubbleShadowSprite ??= Resources.Load<Sprite>(BubbleShadowSpritePath)
                                  ?? GetFallbackBubbleSprite();
            return bubbleShadowSprite;
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
                RectTransform bodyStroke,
                RectTransform body,
                Image bodyImage,
                ScrollRect scrollRect,
                RectTransform highlight,
                RectTransform tailLargeDepth,
                RectTransform tailLargeStroke,
                RectTransform tailLarge,
                RectTransform tailSmallDepth,
                RectTransform tailSmallStroke,
                RectTransform tailSmall,
                RectTransform tailTinyDepth,
                RectTransform tailTinyStroke,
                RectTransform tailTiny,
                RectTransform textContentRect,
                RectTransform textRect,
                Text text)
            {
                Root = root;
                CanvasGroup = canvasGroup;
                BodyShadow = bodyShadow;
                BodyDepth = bodyDepth;
                BodyStroke = bodyStroke;
                Body = body;
                BodyImage = bodyImage;
                ScrollRect = scrollRect;
                Highlight = highlight;
                TailLargeDepth = tailLargeDepth;
                TailLargeStroke = tailLargeStroke;
                TailLarge = tailLarge;
                TailSmallDepth = tailSmallDepth;
                TailSmallStroke = tailSmallStroke;
                TailSmall = tailSmall;
                TailTinyDepth = tailTinyDepth;
                TailTinyStroke = tailTinyStroke;
                TailTiny = tailTiny;
                TextContentRect = textContentRect;
                TextRect = textRect;
                Text = text;
            }

            public RectTransform Root { get; }

            public CanvasGroup CanvasGroup { get; }

            public RectTransform BodyShadow { get; }

            public RectTransform BodyDepth { get; }

            public RectTransform BodyStroke { get; }

            public RectTransform Body { get; }

            public Image BodyImage { get; }

            public ScrollRect ScrollRect { get; }

            public RectTransform Highlight { get; }

            public RectTransform TailLargeDepth { get; }

            public RectTransform TailLargeStroke { get; }

            public RectTransform TailLarge { get; }

            public RectTransform TailSmallDepth { get; }

            public RectTransform TailSmallStroke { get; }

            public RectTransform TailSmall { get; }

            public RectTransform TailTinyDepth { get; }

            public RectTransform TailTinyStroke { get; }

            public RectTransform TailTiny { get; }

            public RectTransform TextContentRect { get; }

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
