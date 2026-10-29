using System.Windows;
using System.Windows.Markup;

namespace Mica {
 // One look for every Mica pop-up, matching the island, login dropdown and save card:
 // soft rounded controls, small type, colour only where it means something.
 static class DialogStyles {
  const string Xaml=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='Button'>
  <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='Transparent'/><Setter Property='FontSize' Value='12'/>
  <Setter Property='Padding' Value='14,8'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='HorizontalContentAlignment' Value='Center'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Grid><Border x:Name='Face' CornerRadius='9' Background='{TemplateBinding Background}'/><Border x:Name='Wash' CornerRadius='9' Background='{DynamicResource Hover}' Opacity='0'/>
    <Border x:Name='Ring' CornerRadius='9' BorderBrush='{DynamicResource Accent}' BorderThickness='1.5' Opacity='0'/>
    <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' RecognizesAccessKey='True'/></Grid>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Wash' Property='Opacity' Value='0.7'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='Wash' Property='Opacity' Value='1'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Ring' Property='Opacity' Value='0.6'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
   </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='TextBox'>
  <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='{DynamicResource Canvas}'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
  <Setter Property='CaretBrush' Value='{DynamicResource Accent}'/><Setter Property='SelectionBrush' Value='#C4B4DA'/><Setter Property='Padding' Value='10,7'/><Setter Property='FontSize' Value='12'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
   <Border x:Name='Box' CornerRadius='9' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'><ScrollViewer x:Name='PART_ContentHost' Style='{x:Null}' Padding='0' Focusable='False' HorizontalScrollBarVisibility='{TemplateBinding HorizontalScrollBarVisibility}' VerticalScrollBarVisibility='{TemplateBinding VerticalScrollBarVisibility}'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
    <Trigger Property='IsReadOnly' Value='True'><Setter TargetName='Box' Property='Background' Value='{DynamicResource Hover}'/><Setter TargetName='Box' Property='BorderThickness' Value='0'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='PasswordBox'>
  <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='{DynamicResource Canvas}'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='Padding' Value='10,7'/>
  <Setter Property='CaretBrush' Value='{DynamicResource Accent}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='PasswordBox'>
   <Border x:Name='Box' CornerRadius='9' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'><ScrollViewer x:Name='PART_ContentHost' Style='{x:Null}' Padding='0' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden' VerticalAlignment='Center'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='CheckBox'>
  <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='FontSize' Value='12'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
   <Grid Background='Transparent'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions>
    <Border x:Name='Box' Width='18' Height='18' CornerRadius='6' BorderThickness='1.5' BorderBrush='{DynamicResource Line}' Background='{DynamicResource Canvas}' VerticalAlignment='Top' Margin='0,1,10,0'>
     <TextBlock x:Name='Tick' Text='&#xE73E;' FontFamily='Segoe MDL2 Assets' FontSize='10' Foreground='{DynamicResource Canvas}' HorizontalAlignment='Center' VerticalAlignment='Center' Opacity='0'/></Border>
    <ContentPresenter Grid.Column='1' VerticalAlignment='Center'/></Grid>
   <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='{DynamicResource Accent}'/><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Accent}'/><Setter TargetName='Tick' Property='Opacity' Value='1'/></Trigger>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
   </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBoxItem'>
  <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Padding' Value='10,7'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>
   <Border x:Name='Row' CornerRadius='7' Padding='{TemplateBinding Padding}' Background='Transparent'><ContentPresenter/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter Property='FontWeight' Value='SemiBold'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBox'>
  <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='FontSize' Value='12'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
   <Grid>
    <ToggleButton x:Name='Toggle' Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
     <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'>
      <Border x:Name='Box' CornerRadius='9' BorderThickness='1' BorderBrush='{DynamicResource Line}' Background='{DynamicResource Canvas}'>
       <TextBlock Text='&#xE70D;' FontFamily='Segoe MDL2 Assets' FontSize='9' Foreground='{DynamicResource Muted}' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
       <Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger></ControlTemplate.Triggers>
     </ControlTemplate></ToggleButton.Template></ToggleButton>
    <ContentPresenter IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='12,8,32,8' VerticalAlignment='Center' HorizontalAlignment='Left'/>
    <Popup x:Name='PART_Popup' Placement='Bottom' VerticalOffset='4' AllowsTransparency='True' Focusable='False' PopupAnimation='None' IsOpen='{TemplateBinding IsDropDownOpen}'>
     <Border MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' CornerRadius='10' BorderThickness='1' BorderBrush='{DynamicResource Line}' Background='{DynamicResource Panel}' Padding='5' Margin='0,0,10,12'>
      <Border.Effect><DropShadowEffect BlurRadius='18' ShadowDepth='4' Opacity='0.16' Color='#252032'/></Border.Effect>
      <ScrollViewer MaxHeight='260' VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer></Border>
    </Popup>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>";
  internal static ResourceDictionary Create() {return (ResourceDictionary)XamlReader.Parse(Xaml);}
 }
}
