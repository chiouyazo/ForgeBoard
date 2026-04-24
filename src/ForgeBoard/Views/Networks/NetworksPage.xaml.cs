using ForgeBoard.Contracts.Models;
using ForgeBoard.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ForgeBoard.Views.Networks;

public sealed partial class NetworksPage : Page
{
    private readonly NetworkListViewModel _viewModel;

    public NetworksPage()
    {
        this.InitializeComponent();
        _viewModel = new NetworkListViewModel(App.ApiClient);
        this.DataContext = _viewModel;
        this.Loaded += async (s, e) => await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void AddNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedFeed is null || string.IsNullOrEmpty(_viewModel.SelectedRepository))
        {
            Shell.Current?.ShowWarning("Select a feed and repository first");
            return;
        }

        NetworkDefinition network = new NetworkDefinition();
        bool saved = await ShowNetworkDialog("Add Network", network);
        if (saved)
        {
            await _viewModel.CreateNetworkCommand.ExecuteAsync(network);
        }
    }

    private async void EditNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string networkId)
        {
            NetworkDefinition? existing = null;
            foreach (NetworkDefinition n in _viewModel.Networks)
            {
                if (n.Id == networkId)
                {
                    existing = n;
                    break;
                }
            }
            if (existing is null)
                return;

            NetworkDefinition edited = new NetworkDefinition
            {
                Id = existing.Id,
                Name = existing.Name,
                SwitchType = existing.SwitchType,
                PhysicalAdapter = existing.PhysicalAdapter,
                AllowManagementOs = existing.AllowManagementOs,
                NatSubnet = existing.NatSubnet,
                NatGateway = existing.NatGateway,
                DhcpRangeStart = existing.DhcpRangeStart,
                DhcpRangeEnd = existing.DhcpRangeEnd,
                VlanId = existing.VlanId,
            };

            bool saved = await ShowNetworkDialog("Edit Network", edited);
            if (saved)
            {
                await _viewModel.UpdateNetworkCommand.ExecuteAsync(edited);
            }
        }
    }

    private async void DeleteNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string networkId)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Delete Network",
                Content = $"Remove network definition '{networkId}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _viewModel.DeleteNetworkCommand.ExecuteAsync(networkId);
            }
        }
    }

    private async void RefreshNetworks_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadNetworksCommand.ExecuteAsync(null);
    }

    private async Task<bool> ShowNetworkDialog(string title, NetworkDefinition network)
    {
        StackPanel panel = new StackPanel { Spacing = 8, MinWidth = 450 };

        TextBox nameBox = new TextBox
        {
            Header = "Name",
            Text = network.Name,
            FontSize = 12,
        };
        panel.Children.Add(nameBox);

        TextBox idBox = new TextBox
        {
            Header = "ID (unique, lowercase, no spaces)",
            Text = network.Id,
            FontSize = 12,
            PlaceholderText = "internal-nat",
        };
        idBox.BeforeTextChanging += (s, args) =>
        {
            args.Cancel = args.NewText.Any(c => c == ' ' || char.IsUpper(c));
        };
        panel.Children.Add(idBox);

        ComboBox typeBox = new ComboBox
        {
            Header = "Switch Type",
            ItemsSource = Enum.GetValues<NetworkSwitchType>(),
            SelectedItem = network.SwitchType,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        panel.Children.Add(typeBox);

        ComboBox physicalAdapterCombo = new ComboBox
        {
            Header = "Physical Adapter",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEditable = true,
        };
        physicalAdapterCombo.Items.Add("auto");
        physicalAdapterCombo.Items.Add("auto-wireless");
        string currentAdapter = network.PhysicalAdapter ?? "auto";
        if (!physicalAdapterCombo.Items.Contains(currentAdapter))
        {
            physicalAdapterCombo.Items.Add(currentAdapter);
        }
        physicalAdapterCombo.SelectedItem = currentAdapter;
        panel.Children.Add(physicalAdapterCombo);

        TextBlock adapterHint = new TextBlock
        {
            Text =
                "auto = fastest wired adapter, auto-wireless = includes Wi-Fi. Custom: name:Ethernet 2 (exact match) or description:Intel* (wildcard)",
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["HintBrush"],
        };
        panel.Children.Add(adapterHint);

        CheckBox allowMgmtBox = new CheckBox
        {
            Content = "Allow host OS to share this adapter",
            IsChecked = network.AllowManagementOs,
        };
        panel.Children.Add(allowMgmtBox);

        TextBox natSubnetBox = new TextBox
        {
            Header = "NAT Subnet",
            Text = network.NatSubnet ?? string.Empty,
            FontSize = 12,
            PlaceholderText = "192.168.100.0/24",
        };
        panel.Children.Add(natSubnetBox);

        TextBox natGatewayBox = new TextBox
        {
            Header = "NAT Gateway",
            Text = network.NatGateway ?? string.Empty,
            FontSize = 12,
            PlaceholderText = "192.168.100.1",
        };
        panel.Children.Add(natGatewayBox);

        TextBox dhcpStartBox = new TextBox
        {
            Header = "DHCP Range Start",
            Text = network.DhcpRangeStart ?? string.Empty,
            FontSize = 12,
            PlaceholderText = "192.168.100.100",
        };
        panel.Children.Add(dhcpStartBox);

        TextBox dhcpEndBox = new TextBox
        {
            Header = "DHCP Range End",
            Text = network.DhcpRangeEnd ?? string.Empty,
            FontSize = 12,
            PlaceholderText = "192.168.100.200",
        };
        panel.Children.Add(dhcpEndBox);

        TextBox vlanBox = new TextBox
        {
            Header = "VLAN ID (optional)",
            Text = network.VlanId?.ToString() ?? string.Empty,
            FontSize = 12,
        };
        vlanBox.BeforeTextChanging += (s, args) =>
        {
            args.Cancel = !string.IsNullOrEmpty(args.NewText) && !args.NewText.All(char.IsDigit);
        };
        panel.Children.Add(vlanBox);

        Action updateVisibility = () =>
        {
            NetworkSwitchType selectedType = typeBox.SelectedItem is NetworkSwitchType t
                ? t
                : NetworkSwitchType.Internal;
            bool isExternal = selectedType == NetworkSwitchType.External;
            bool isNat = selectedType == NetworkSwitchType.NAT;

            physicalAdapterCombo.Visibility = isExternal
                ? Visibility.Visible
                : Visibility.Collapsed;
            adapterHint.Visibility = isExternal ? Visibility.Visible : Visibility.Collapsed;
            allowMgmtBox.Visibility = isExternal ? Visibility.Visible : Visibility.Collapsed;
            natSubnetBox.Visibility = isNat ? Visibility.Visible : Visibility.Collapsed;
            natGatewayBox.Visibility = isNat ? Visibility.Visible : Visibility.Collapsed;
            dhcpStartBox.Visibility = isNat ? Visibility.Visible : Visibility.Collapsed;
            dhcpEndBox.Visibility = isNat ? Visibility.Visible : Visibility.Collapsed;
        };

        typeBox.SelectionChanged += (s, args) => updateVisibility();
        updateVisibility();

        ContentDialog dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 500,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            network.Name = nameBox.Text;
            network.Id = string.IsNullOrWhiteSpace(idBox.Text)
                ? nameBox.Text.ToLowerInvariant().Replace(" ", "-")
                : idBox.Text.Trim();
            network.SwitchType = typeBox.SelectedItem is NetworkSwitchType st
                ? st
                : NetworkSwitchType.Internal;
            string adapterValue = physicalAdapterCombo.SelectedItem?.ToString() ?? string.Empty;
            network.PhysicalAdapter = string.IsNullOrWhiteSpace(adapterValue) ? null : adapterValue;
            network.AllowManagementOs = allowMgmtBox.IsChecked == true;
            network.NatSubnet = string.IsNullOrWhiteSpace(natSubnetBox.Text)
                ? null
                : natSubnetBox.Text;
            network.NatGateway = string.IsNullOrWhiteSpace(natGatewayBox.Text)
                ? null
                : natGatewayBox.Text;
            network.DhcpRangeStart = string.IsNullOrWhiteSpace(dhcpStartBox.Text)
                ? null
                : dhcpStartBox.Text;
            network.DhcpRangeEnd = string.IsNullOrWhiteSpace(dhcpEndBox.Text)
                ? null
                : dhcpEndBox.Text;
            network.VlanId = int.TryParse(vlanBox.Text, out int vlan) ? vlan : null;
            return true;
        }

        return false;
    }

    private async void AddFeed_Click(object sender, RoutedEventArgs e)
    {
        StackPanel panel = new StackPanel { Spacing = 8 };
        TextBox nameBox = new TextBox { Header = "Name", FontSize = 12 };
        TextBox urlBox = new TextBox
        {
            Header = "Connection URL",
            FontSize = 12,
            PlaceholderText = "https://nexus.example.com:8081",
        };
        TextBox userBox = new TextBox { Header = "Username (optional)", FontSize = 12 };
        PasswordBox passBox = new PasswordBox { Header = "Password (optional)" };

        panel.Children.Add(nameBox);
        panel.Children.Add(urlBox);
        panel.Children.Add(userBox);
        panel.Children.Add(passBox);

        ContentDialog dialog = new ContentDialog
        {
            Title = "Add Nexus Feed",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            Feed feed = new Feed
            {
                Name = nameBox.Text,
                SourceType = FeedType.Nexus,
                ConnectionString = urlBox.Text,
                Username = string.IsNullOrWhiteSpace(userBox.Text) ? null : userBox.Text,
                Password = string.IsNullOrWhiteSpace(passBox.Password) ? null : passBox.Password,
            };
            await _viewModel.AddFeedCommand.ExecuteAsync(feed);
        }
    }

    private async void TestFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            await _viewModel.TestFeedCommand.ExecuteAsync(id);
        }
    }

    private async void DeleteFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Delete Feed",
                Content = "Remove this feed?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _viewModel.DeleteFeedCommand.ExecuteAsync(id);
            }
        }
    }
}
