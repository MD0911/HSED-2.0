using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HSED_2_0;

namespace HSED_2._0;

public partial class RuntimeErrorListDialog : Window
{
    private readonly IReadOnlyList<RuntimeErrorEntry> _errors;

    public RuntimeErrorListDialog()
        : this(Array.Empty<RuntimeErrorEntry>())
    {
    }

    public RuntimeErrorListDialog(IReadOnlyList<RuntimeErrorEntry> errors)
    {
        _errors = errors;
        InitializeComponent();
        PopulateErrors();
    }

    private void PopulateErrors()
    {
        ErrorCountText.Text = _errors.Count == 1 ? "1 Fehler vorhanden" : $"{_errors.Count} Fehler vorhanden";

        foreach (var error in _errors)
        {
            var button = new Button
            {
                Classes = { "errorItem" },
                Content = error.Title,
                Tag = error
            };

            button.Click += Error_Click;
            ErrorListPanel.Children.Add(button);
        }
    }

    private void Error_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RuntimeErrorEntry error })
            return;

        var detailDialog = new RuntimeErrorDetailDialog(error);
        detailDialog.Show(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
