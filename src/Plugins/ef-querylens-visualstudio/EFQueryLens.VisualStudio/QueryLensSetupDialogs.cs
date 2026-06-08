// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

internal static class QueryLensSetupDialogs
{
    private sealed class ProviderOption
    {
        internal ProviderOption(string label, string provider)
        {
            Label = label;
            Provider = provider;
        }

        internal string Label { get; }

        internal string Provider { get; }
    }

    private static readonly ProviderOption[] ProviderOptions =
    {
        new ProviderOption("SQL Server", "SqlServer"),
        new ProviderOption("PostgreSQL (Npgsql)", "Npgsql"),
        new ProviderOption("MySQL (Pomelo)", "MySql"),
        new ProviderOption("SQLite", "Sqlite"),
    };

    internal static string? PickHost(IReadOnlyList<QueryLensLanguageClient.SetupHostCandidate> hosts)
    {
        if (hosts.Count == 0)
        {
            return null;
        }

        if (hosts.Count == 1)
        {
            return hosts[0].ProjectPath;
        }

        using var form = new Form
        {
            Text = "EF QueryLens — Select Host Project",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(640, 360),
        };

        var label = new Label
        {
            Text = "Select the executable host project for the QueryLens factory:",
            AutoSize = false,
            Bounds = new Rectangle(12, 12, 616, 32),
        };

        var listItems = new List<HostListItem>(hosts.Count);
        foreach (var host in hosts)
        {
            listItems.Add(new HostListItem(host));
        }

        var listBox = new ListBox
        {
            Bounds = new Rectangle(12, 48, 616, 260),
            DisplayMember = nameof(HostListItem.Display),
            DataSource = listItems,
        };

        if (listBox.Items.Count > 0)
        {
            listBox.SelectedIndex = 0;
        }

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Bounds = new Rectangle(452, 318, 84, 28),
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(544, 318, 84, 28),
        };

        form.Controls.Add(label);
        form.Controls.Add(listBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        if (form.ShowDialog() != DialogResult.OK || listBox.SelectedItem is not HostListItem selected)
        {
            return null;
        }

        return selected.Host.ProjectPath;
    }

    internal static string? PickProvider()
    {
        using var form = new Form
        {
            Text = "EF QueryLens — Select EF Provider",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(480, 280),
        };

        var label = new Label
        {
            Text = "Select the EF Core provider for the generated factory:",
            AutoSize = false,
            Bounds = new Rectangle(12, 12, 456, 32),
        };

        var listBox = new ListBox
        {
            Bounds = new Rectangle(12, 48, 456, 180),
            DisplayMember = nameof(ProviderOption.Label),
            DataSource = ProviderOptions,
        };

        listBox.SelectedIndex = 0;

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Bounds = new Rectangle(292, 238, 84, 28),
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(384, 238, 84, 28),
        };

        form.Controls.Add(label);
        form.Controls.Add(listBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        if (form.ShowDialog() != DialogResult.OK || listBox.SelectedItem is not ProviderOption selected)
        {
            return null;
        }

        return selected.Provider;
    }

    private sealed class HostListItem
    {
        internal HostListItem(QueryLensLanguageClient.SetupHostCandidate host)
        {
            Host = host;
        }

        internal QueryLensLanguageClient.SetupHostCandidate Host { get; }

        internal string Display =>
            string.IsNullOrWhiteSpace(Host.AssemblyPath)
                ? $"{Host.DisplayName} — {Host.ProjectPath}"
                : $"{Host.DisplayName} — {Host.ProjectPath} ({Host.AssemblyPath})";

        public override string ToString() => Display;
    }
}
