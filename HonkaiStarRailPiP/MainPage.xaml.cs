using System;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.Graphics.Canvas;
using Windows.UI.Composition;
using Microsoft.Graphics.Canvas.UI.Composition;
using System.Numerics;
using Windows.Graphics.DirectX;
using Windows.UI.Xaml.Hosting;
using System.Threading.Tasks;
using Windows.UI;
using Windows.ApplicationModel.Core;
using Windows.UI.ViewManagement;
using Windows.Security.Authorization.AppCapabilityAccess;
using Windows.UI.Xaml.Navigation;

namespace HonkaiStarRailPiP
{
    public sealed partial class MainPage : Page
    {
        private SizeInt32 lastSize;
        private GraphicsCaptureItem captureItem;
        private Direct3D11CaptureFramePool framePool;
        private GraphicsCaptureSession session;

        private CanvasDevice canvasDevice;
        private CompositionGraphicsDevice compositionGraphicsDevice;
        private Compositor compositor;
        private CompositionDrawingSurface surface;
        private CanvasBitmap currentFrame;

        private Boolean allowBorderless;

        public MainPage()
        {
            this.InitializeComponent();

            // ウィンドウのサイズを指定する
            ApplicationView.PreferredLaunchViewSize = new Size(10, 10);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;

            canvasDevice = new CanvasDevice();

            compositionGraphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(Window.Current.Compositor, canvasDevice);

            compositor = Window.Current.Compositor;

            surface = compositionGraphicsDevice.CreateDrawingSurface(new Size(400, 400), DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);

            var visual = compositor.CreateSpriteVisual();
            visual.RelativeSizeAdjustment = Vector2.One;
            var brush = compositor.CreateSurfaceBrush(surface);
            brush.HorizontalAlignmentRatio = 0.5f;
            brush.VerticalAlignmentRatio = 0.5f;
            brush.Stretch = CompositionStretch.Uniform;
            visual.Brush = brush;
            ElementCompositionPreview.SetElementChildVisual(this, visual);

            // ウィンドウのタイトルバーを隠す
            CoreApplicationViewTitleBar titleBar = CoreApplication.GetCurrentView().TitleBar;
            titleBar.ExtendViewIntoTitleBar = true;
        }

        /*
         * キャプチャを開始する
         */
        public async Task StartCaptureAsync()
        {
            // 崩壊スターレイルの画面を選択
            var picker = new GraphicsCapturePicker();
            GraphicsCaptureItem item = await picker.PickSingleItemAsync();

            // 正しく画面が選択されたらキャプチャを開始する
            if (item != null)
            {
                // 常に最前面に表示するようにする
                await ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.CompactOverlay);

                // キャプチャの開始
                StartCaptureInternal(item);
            }
        }

        // 内部のキャプチャを開始する
        private void StartCaptureInternal(GraphicsCaptureItem item)
        {
            StopCapture();

            captureItem = item;
            lastSize = captureItem.Size;

            framePool = Direct3D11CaptureFramePool.Create(canvasDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, captureItem.Size);

            framePool.FrameArrived += (s, a) =>
            {
                using (var frame = framePool.TryGetNextFrame())
                {
                    ProcessFrame(frame);
                }
            };

            captureItem.Closed += (s, a) =>
            {
                StopCapture();
            };

            session = framePool.CreateCaptureSession(captureItem);

            // マウスカーソルを非表示にする
            session.IsCursorCaptureEnabled = false;

            // 許可されたなら黄色の枠を消す
            if (allowBorderless)
            {
                session.IsBorderRequired = false;
            }

            // キャプチャーの開始
            session.StartCapture();
        }

        // キャプチャを停止させる
        public void StopCapture()
        {
            session?.Dispose();
            framePool?.Dispose();
            captureItem = null;
            session = null;
            framePool = null;
        }

        // フレームの処理
        private void ProcessFrame(Direct3D11CaptureFrame frame)
        {
            bool needsReset = false;
            bool recreateDevice = false;

            if ((frame.ContentSize.Width != lastSize.Width) ||
                (frame.ContentSize.Height != lastSize.Height))
            {
                needsReset = true;
                lastSize = frame.ContentSize;
            }

            try
            {
                CanvasBitmap canvasBitmap = CanvasBitmap.CreateFromDirect3D11Surface(
                    canvasDevice,
                    frame.Surface);

                currentFrame = canvasBitmap;

                FillSurfaceWithBitmap(canvasBitmap);
            }

            catch (Exception e) when (canvasDevice.IsDeviceLost(e.HResult))
            {
                needsReset = true;
                recreateDevice = true;
            }

            if (needsReset)
            {
                ResetFramePool(frame.ContentSize, recreateDevice);
            }
        }

        // Bitmapを使って画面に表示させる
        private void FillSurfaceWithBitmap(CanvasBitmap canvasBitmap)
        {
            CanvasComposition.Resize(surface, canvasBitmap.Size);

            using (var session = CanvasComposition.CreateDrawingSession(surface))
            {
                session.Clear(Colors.Transparent);
                session.DrawImage(canvasBitmap);
            }
        }

        // フレームプールをリセットする
        private void ResetFramePool(SizeInt32 size, bool recreateDevice)
        {
            do
            {
                try
                {
                    if (recreateDevice)
                    {
                        canvasDevice = new CanvasDevice();
                    }

                    framePool.Recreate(
                        canvasDevice,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        size);
                }
                catch (Exception e) when (canvasDevice.IsDeviceLost(e.HResult))
                {
                    canvasDevice = null;
                    recreateDevice = true;
                }
            } while (canvasDevice == null);
        }

        // ボタンがクリックされたらキャプチャーを開始する
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            // 許可を取らないと黄色い枠が出る
            AppCapabilityAccessStatus appCapabilityAccessStatus = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            allowBorderless = appCapabilityAccessStatus == AppCapabilityAccessStatus.Allowed;

            await StartCaptureAsync();
        }
    }
}
