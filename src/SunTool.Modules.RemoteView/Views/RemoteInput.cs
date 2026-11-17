using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SunTool.Modules.RemoteView.Logic;
using SunTool.Modules.RemoteView.ViewModels;

namespace SunTool.Modules.RemoteView.Views;

/// <summary>
/// 원격 화면 이미지 위의 마우스·키보드를 잡아 뷰모델로 넘깁니다 (제어 중일 때만).
/// 인라인 뷰와 전체 화면 창에서 함께 씁니다. WPF Key → Win32 VK 는 KeyInterop 로 바로 바꿉니다.
/// </summary>
public static class RemoteInput
{
    public static void Attach(Image image, UIElement keySource, RemoteViewViewModel vm)
    {
        image.Focusable = true;

        image.PreviewMouseMove += (_, e) =>
        {
            if (!vm.IsControlling)
            {
                return;
            }

            if (TryUnit(image, e, out var u, out var v))
            {
                vm.SendMouseInput(RemoteProtocol.MouseAction.Move, u, v);
            }
        };

        image.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!vm.IsControlling)
            {
                return;
            }

            image.Focus();
            Mouse.Capture(image);
            if (TryUnit(image, e, out var u, out var v))
            {
                vm.SendMouseInput(RemoteProtocol.MouseAction.LeftDown, u, v);
            }

            e.Handled = true;
        };

        image.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!vm.IsControlling)
            {
                return;
            }

            if (TryUnit(image, e, out var u, out var v))
            {
                vm.SendMouseInput(RemoteProtocol.MouseAction.LeftUp, u, v);
            }

            if (ReferenceEquals(Mouse.Captured, image))
            {
                Mouse.Capture(null);
            }

            e.Handled = true;
        };

        image.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (!vm.IsControlling)
            {
                return;
            }

            if (TryUnit(image, e, out var u, out var v))
            {
                vm.SendMouseInput(RemoteProtocol.MouseAction.RightDown, u, v);
            }

            e.Handled = true;
        };

        image.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (!vm.IsControlling)
            {
                return;
            }

            if (TryUnit(image, e, out var u, out var v))
            {
                vm.SendMouseInput(RemoteProtocol.MouseAction.RightUp, u, v);
            }

            e.Handled = true;
        };

        image.PreviewMouseDown += (_, e) =>
        {
            if (!vm.IsControlling || e.ChangedButton != MouseButton.Middle)
            {
                return;
            }

            if (TryUnit(image, e, out var u, out var v))
            {
                vm.SendMouseInput(RemoteProtocol.MouseAction.MiddleDown, u, v);
            }

            e.Handled = true;
        };

        image.PreviewMouseUp += (_, e) =>
        {
            if (!vm.IsControlling || e.ChangedButton != MouseButton.Middle)
            {
                return;
            }

            if (TryUnit(image, e, out var u, out var v))
            {
                vm.SendMouseInput(RemoteProtocol.MouseAction.MiddleUp, u, v);
            }

            e.Handled = true;
        };

        image.PreviewMouseWheel += (_, e) =>
        {
            if (!vm.IsControlling)
            {
                return;
            }

            if (TryUnit(image, e, out var u, out var v))
            {
                vm.SendMouseInput(RemoteProtocol.MouseAction.Wheel, u, v, e.Delta);
            }

            e.Handled = true;
        };

        keySource.PreviewKeyDown += (_, e) => OnKey(vm, e, down: true);
        keySource.PreviewKeyUp += (_, e) => OnKey(vm, e, down: false);

        // 한/영(VK 0x15)·한자(0x19) 토글은 WPF 관리형 라우팅이 삼켜(ImeProcessed, 복원 키 없음) 전송되지 않습니다.
        // 창의 원시 메시지(WM_KEYDOWN/UP)에서 직접 가로채 전송합니다.
        if (keySource is FrameworkElement fe)
        {
            var hookInstalled = false;
            void InstallImeKeyHook()
            {
                if (hookInstalled || PresentationSource.FromVisual(fe) is not HwndSource src)
                {
                    return;
                }

                src.AddHook((IntPtr _, int msg, IntPtr wParam, IntPtr _, ref bool handled) =>
                {
                    ImeKeyHook(vm, msg, wParam, ref handled);
                    return IntPtr.Zero;
                });
                hookInstalled = true;
            }

            if (fe.IsLoaded)
            {
                InstallImeKeyHook();
            }

            fe.Loaded += (_, _) => InstallImeKeyHook();
        }
    }

    // 한/영(0x15)·한자(0x19) 토글 키만 원시 메시지에서 직접 전송합니다. handled 로 로컬 IME 전환은 막습니다.
    private static void ImeKeyHook(RemoteViewViewModel vm, int msg, IntPtr wParam, ref bool handled)
    {
        if (!vm.IsControlling)
        {
            return;
        }

        var vk = (int)wParam;
        if (vk != 0x15 && vk != 0x19)
        {
            return;
        }

        switch (msg)
        {
            case 0x0100: // WM_KEYDOWN
            case 0x0104: // WM_SYSKEYDOWN
                vm.SendKeyInput((ushort)vk, down: true);
                handled = true;
                break;
            case 0x0101: // WM_KEYUP
            case 0x0105: // WM_SYSKEYUP
                vm.SendKeyInput((ushort)vk, down: false);
                handled = true;
                break;
        }
    }

    private static void OnKey(RemoteViewViewModel vm, KeyEventArgs e, bool down)
    {
        if (!vm.IsControlling)
        {
            return;
        }

        // Alt 는 SystemKey 로, 한/영·한자 등 IME 가 가로챈 키는 ImeProcessedKey 로 원래 키를 복원합니다.
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };

        // Esc 는 전체 화면 탈출용으로 예약 — 원격으로 보내지 않고 통과시킵니다(항상 빠져나갈 수 있게).
        if (key == Key.Escape)
        {
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk > 0)
        {
            vm.SendKeyInput((ushort)vk, down);
            e.Handled = true; // Tab·Alt·방향키가 로컬 UI 로 새지 않도록.
        }
    }

    private static bool TryUnit(Image image, MouseEventArgs e, out double u, out double v)
    {
        u = 0;
        v = 0;
        if (image.Source is not BitmapSource bmp)
        {
            return false;
        }

        var p = e.GetPosition(image);
        var (uu, vv, inside) = InputMapping.DisplayToUnit(p.X, p.Y, image.ActualWidth, image.ActualHeight, bmp.PixelWidth, bmp.PixelHeight);
        u = uu;
        v = vv;
        return inside;
    }
}
