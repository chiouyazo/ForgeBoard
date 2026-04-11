using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace ForgeBoard.Controls;

public sealed partial class WizardStepper : UserControl
{
    private static readonly string[] StepLabels =
    {
        "Base Image",
        "Build Steps",
        "Configuration",
        "Review",
    };

    public static readonly DependencyProperty CurrentStepProperty = DependencyProperty.Register(
        nameof(CurrentStep),
        typeof(int),
        typeof(WizardStepper),
        new PropertyMetadata(0, OnCurrentStepChanged)
    );

    public int CurrentStep
    {
        get => (int)GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    public WizardStepper()
    {
        this.InitializeComponent();
        this.Loaded += (s, e) => Render();
    }

    private static void OnCurrentStepChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    )
    {
        WizardStepper stepper = (WizardStepper)d;
        stepper.Render();
    }

    private void Render()
    {
        StepperPanel.Children.Clear();

        SolidColorBrush accentBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 33, 150, 243));
        SolidColorBrush mutedBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 189, 189, 189));
        SolidColorBrush completedBrush = new SolidColorBrush(
            ColorHelper.FromArgb(255, 76, 175, 80)
        );

        for (int i = 0; i < StepLabels.Length; i++)
        {
            if (i > 0)
            {
                Rectangle line = new Rectangle
                {
                    Width = 40,
                    Height = 2,
                    Fill = i <= CurrentStep ? accentBrush : mutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                StepperPanel.Children.Add(line);
            }

            SolidColorBrush circleBrush;
            string circleText;

            if (i < CurrentStep)
            {
                circleBrush = completedBrush;
                circleText = "\uE73E";
            }
            else if (i == CurrentStep)
            {
                circleBrush = accentBrush;
                circleText = (i + 1).ToString();
            }
            else
            {
                circleBrush = mutedBrush;
                circleText = (i + 1).ToString();
            }

            StackPanel stepPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 4,
            };

            Grid circleGrid = new Grid
            {
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            Ellipse circle = new Ellipse
            {
                Width = 28,
                Height = 28,
                Fill = circleBrush,
            };

            TextBlock numberText = new TextBlock
            {
                Text = circleText,
                FontSize = i < CurrentStep ? 14 : 12,
                FontFamily =
                    i < CurrentStep
                        ? new FontFamily("Segoe MDL2 Assets")
                        : new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            circleGrid.Children.Add(circle);
            circleGrid.Children.Add(numberText);

            TextBlock label = new TextBlock
            {
                Text = StepLabels[i],
                FontSize = 10,
                Foreground = i == CurrentStep ? accentBrush : mutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            stepPanel.Children.Add(circleGrid);
            stepPanel.Children.Add(label);

            StepperPanel.Children.Add(stepPanel);
        }
    }
}
