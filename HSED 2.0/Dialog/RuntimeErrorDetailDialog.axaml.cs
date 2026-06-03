using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HSED_2_0;

namespace HSED_2._0;

public partial class RuntimeErrorDetailDialog : Window
{
    public RuntimeErrorDetailDialog()
        : this(new RuntimeErrorEntry(string.Empty, string.Empty, string.Empty, DateTime.UtcNow))
    {
    }

    public RuntimeErrorDetailDialog(RuntimeErrorEntry error)
    {
        InitializeComponent();
        TitleText.Text = error.Title;
        DetailText.Text = error.DetailText;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
