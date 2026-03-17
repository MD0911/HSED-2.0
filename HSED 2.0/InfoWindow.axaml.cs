using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace HSED_2._0;

public partial class InfoWindow : Window
{
    private bool _navBarOpen;
    private readonly StackPanel _infoItemsPanel;

    public InfoWindow()
    {
        InitializeComponent();
        _infoItemsPanel = this.FindControl<StackPanel>("InfoItemsPanel")
            ?? throw new InvalidOperationException("InfoItemsPanel konnte nicht gefunden werden.");
        Position = new PixelPoint(0, 0);
        LoadInfoData();

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void LoadInfoData()
    {
        _infoItemsPanel.Children.Clear();

        var sections = new List<InfoSection>();
        foreach (var fileName in new[] { "info.json", "config.json" })
        {
            var path = ResolveJsonPath(fileName);
            if (path == null)
                continue;

            sections.AddRange(ReadSections(path));
        }

        if (sections.Count == 0)
        {
            _infoItemsPanel.Children.Add(BuildEmptyState());
            return;
        }

        foreach (var section in sections)
            _infoItemsPanel.Children.Add(BuildSectionCard(section));
    }

    private static string? ResolveJsonPath(string fileName)
    {
        var baseDirPath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(baseDirPath))
            return baseDirPath;

        var localPath = Path.Combine(Environment.CurrentDirectory, fileName);
        if (File.Exists(localPath))
            return localPath;

        return null;
    }

    private static IEnumerable<InfoSection> ReadSections(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                var entries = new List<InfoEntry>();
                foreach (var nestedProperty in property.Value.EnumerateObject())
                    entries.Add(new InfoEntry(nestedProperty.Name, GetDisplayValue(nestedProperty.Value)));

                yield return new InfoSection(GetSectionTitle(path, property.Name), entries);
                continue;
            }

            yield return new InfoSection(
                GetSectionTitle(path, Path.GetFileNameWithoutExtension(path)),
                new List<InfoEntry> { new(property.Name, GetDisplayValue(property.Value)) });
        }
    }

    private static string GetSectionTitle(string path, string originalTitle)
    {
        if (string.Equals(Path.GetFileName(path), "config.json", StringComparison.OrdinalIgnoreCase))
            return "Verbindungsinformationen";

        return originalTitle;
    }

    private static string GetDisplayValue(JsonElement element)
    {
        var raw = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => element.ToString()
        };

        return string.IsNullOrWhiteSpace(raw) ? "-" : raw;
    }

    private static Border BuildSectionCard(InfoSection section)
    {
        var card = new Border
        {
            Classes = { "sectionCard" }
        };

        var container = new StackPanel
        {
            Spacing = 12
        };

        container.Children.Add(new TextBlock
        {
            Text = section.Title,
            Classes = { "sectionTitle" }
        });

        foreach (var entry in section.Entries)
            container.Children.Add(BuildInfoRow(entry));

        card.Child = container;
        return card;
    }

    private static Border BuildInfoRow(InfoEntry entry)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0b1220")),
            BorderBrush = new SolidColorBrush(Color.Parse("#1f2a3b")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,3*")
        };

        grid.Children.Add(new TextBlock
        {
            Text = entry.Key,
            Classes = { "keyText" },
            VerticalAlignment = VerticalAlignment.Center
        });

        var valueText = new TextBlock
        {
            Text = entry.Value,
            Classes = { "valueText" },
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);

        row.Child = grid;
        return row;
    }

    private static Border BuildEmptyState()
    {
        return new Border
        {
            Classes = { "sectionCard" },
            Child = new TextBlock
            {
                Text = "Weder info.json noch config.json konnten geladen werden.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private void Button_Click_Settings(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var buttonTag = button.Tag?.ToString() ?? string.Empty;

        if (buttonTag == "Menu")
        {
            ToggleNavBar();
            return;
        }

        switch (buttonTag)
        {
            case "Settings":
            case "Menu":
                (Owner as Window)?.Show();
                Hide();
                break;
        }
    }

    private void ToggleNavBar()
    {
        if (!_navBarOpen)
        {
            NavBar.Width = 160;
            StackPanelNavBar.HorizontalAlignment = HorizontalAlignment.Left;
            StackPanelNavBar.Margin = new Thickness(10, 25, 0, 0);

            SettingsText.IsVisible = true;
            ButtonSettings.Width = 100;
            SettingsText2.IsVisible = true;
            ButtonSettings2.Width = 100;
            SettingsText3.IsVisible = true;
            ButtonSettings3.Width = 100;
            SettingsText4.IsVisible = true;
            ButtonSettings4.Width = 100;
            SettingsText6.IsVisible = true;
            ButtonSettings6.Width = 100;
            SettingsText7.IsVisible = true;
            ButtonSettings7.Width = 100;
            Overlap.IsVisible = true;

            _navBarOpen = true;
        }
        else
        {
            NavBar.Width = 60;
            StackPanelNavBar.HorizontalAlignment = HorizontalAlignment.Center;
            StackPanelNavBar.Margin = new Thickness(0, 25, 0, 0);

            SettingsText.IsVisible = false;
            ButtonSettings.Width = 50;
            SettingsText2.IsVisible = false;
            ButtonSettings2.Width = 50;
            SettingsText3.IsVisible = false;
            ButtonSettings3.Width = 50;
            SettingsText4.IsVisible = false;
            ButtonSettings4.Width = 50;
            SettingsText6.IsVisible = false;
            ButtonSettings6.Width = 50;
            SettingsText7.IsVisible = false;
            ButtonSettings7.Width = 50;
            Overlap.IsVisible = false;

            _navBarOpen = false;
        }
    }

    private sealed record InfoSection(string Title, IReadOnlyList<InfoEntry> Entries);

    private sealed record InfoEntry(string Key, string Value);
}
