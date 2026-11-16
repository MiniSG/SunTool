using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    }

    private static void OnKey(RemoteViewViewModel vm, KeyEventArgs e, bool down)
    {
        if (!vm.IsControlling)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

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
