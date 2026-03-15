using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HSED_2._0.ViewModels;

namespace HSED_2._0.Controls;

public partial class OnScreenKeyboard : UserControl
{
    private readonly ObservableCollection<string> _row1 = new();
    private readonly ObservableCollection<string> _row2 = new();
    private readonly ObservableCollection<string> _row3 = new();

    private ItemsControl? _row1Items;
    private ItemsControl? _row2Items;
    private ItemsControl? _row3Items;

    private Button? _modeLeftBtn;
    private Button? _modeRightBtn;

    private KeyboardMode _mode = KeyboardMode.Letters;
    private bool _shift;
    private bool _wired;

    public OnScreenKeyboard()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, __) =>
        {
            if (_wired) return;
            _wired = true;

            _row1Items = this.FindControl<ItemsControl>("Row1Items");
            _row2Items = this.FindControl<ItemsControl>("Row2Items");
            _row3Items = this.FindControl<ItemsControl>("Row3Items");
            _modeLeftBtn = this.FindControl<Button>("ModeLeftBtn");
            _modeRightBtn = this.FindControl<Button>("ModeRightBtn");

            if (_row1Items == null || _row2Items == null || _row3Items == null)
                throw new NullReferenceException(
                    "ItemsControls nicht gefunden. Prüfe x:Name=\"Row1Items\", \"Row2Items\", \"Row3Items\"."
                );

            _row1Items.ItemsSource = _row1;
            _row2Items.ItemsSource = _row2;
            _row3Items.ItemsSource = _row3;

            BuildLayout();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private WifiViewModel? GetVm()
        => TopLevel.GetTopLevel(this)?.DataContext as WifiViewModel;

    private void BuildLayout()
    {
        _row1.Clear();
        _row2.Clear();
        _row3.Clear();

        if (_mode == KeyboardMode.Letters)
        {
            // 11 / 11 / 7
            AddRow(_row1, "q w e r t z u i o p ü");
            AddRow(_row2, "a s d f g h j k l ö ä");
            AddRow(_row3, "y x c v b n m");
        }
        else if (_mode == KeyboardMode.Numbers)
        {
            // 11 / 11 / 7  (Mobile typisch)
            AddRow(_row1, "1 2 3 4 5 6 7 8 9 0 -");
            AddRow(_row2, "@ # € _ & + ( ) / * :");
            AddRow(_row3, ". , ? ! ' \" ="); // genau 7
        }
        else // Symbols
        {
            // 11 / 11 / 7
            AddRow(_row1, "[ ] { } < > ^ ~ | \\ `");
            AddRow(_row2, "° · • ✓ × ÷ § © ® ™");
            AddRow(_row3, "+ - _ $ € £ ¥"); // genau 7
        }

        // Bottom-left
        if (_modeLeftBtn != null)
            _modeLeftBtn.Content = _mode == KeyboardMode.Letters ? "123" : "ABC";

        // Bottom-right: in Letters -> #+=, sonst -> 123
        if (_modeRightBtn != null)
            _modeRightBtn.Content = _mode == KeyboardMode.Letters ? "#+=" : "123";
    }


    private void AddRow(ObservableCollection<string> row, string keys)
    {
        foreach (var k in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            row.Add(FormatKeyForDisplay(k));
    }

    private string FormatKeyForDisplay(string k)
    {
        if (_mode != KeyboardMode.Letters) return k;

        if (_shift)
            return k.Length == 1 ? k.ToUpperInvariant() : k;

        return k.ToLowerInvariant();
    }

    private void Key_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        if (b.Content is not string label) return;

        var vm = GetVm();
        if (vm == null) return;

        if (_mode == KeyboardMode.Letters)
        {
            var toWrite = _shift ? label.ToUpperInvariant() : label.ToLowerInvariant();
            vm.AppendKey(toWrite);

            // Shift behaves like mobile: one-shot
            if (_shift)
            {
                _shift = false;
                BuildLayout();
            }
            return;
        }

        vm.AppendKey(label);
    }

    private void Shift_Click(object? sender, RoutedEventArgs e)
    {
        if (_mode != KeyboardMode.Letters) return;
        _shift = !_shift;
        BuildLayout();
    }

    private void Space_Click(object? sender, RoutedEventArgs e)
        => GetVm()?.AppendKey(" ");

    private void Enter_Click(object? sender, RoutedEventArgs e)
        => GetVm()?.AppendKey("\n");

    private void Backspace_Click(object? sender, RoutedEventArgs e)
        => GetVm()?.Backspace();

    private void ModeLeft_Click(object? sender, RoutedEventArgs e)
    {
        _mode = _mode == KeyboardMode.Letters ? KeyboardMode.Numbers : KeyboardMode.Letters;
        _shift = false;
        BuildLayout();
    }

    // In Letters this shows 😊, but we still use it to switch numbers/symbols like before
    private void ModeRight_Click(object? sender, RoutedEventArgs e)
    {
        if (_mode == KeyboardMode.Letters)
            _mode = KeyboardMode.Symbols;
        else
            _mode = KeyboardMode.Numbers;

        _shift = false;
        BuildLayout();
    }



    private enum KeyboardMode
    {
        Letters,
        Numbers,
        Symbols
    }
}
