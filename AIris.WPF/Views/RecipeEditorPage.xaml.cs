using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 配方编辑器 glue：DataContext 绑 VM + ROI 预览 Canvas 绘制（0~1000 归一化 → 画布比例）。
    /// 数值标画（X/Y/W/H 文本框）驱动的框叠加；鼠标拖拽标画为后续增强（Core 模型已按归一化口径备好）。
    /// </summary>
    public partial class RecipeEditorPage : UserControl
    {
        private RecipeEditorViewModel _vm;
        private readonly Brush _selBrush = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));
        private readonly Brush _dimBrush = new SolidColorBrush(Color.FromArgb(140, 0x9A, 0x8F, 0x86));

        public RecipeEditorPage()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (_vm != null)
                {
                    _vm.PropertyChanged -= Vm_PropertyChanged;
                    _vm.Rois.CollectionChanged -= Rois_CollectionChanged;
                }
                _vm = DataContext as RecipeEditorViewModel;
                if (_vm != null)
                {
                    _vm.PropertyChanged += Vm_PropertyChanged;
                    _vm.Rois.CollectionChanged += Rois_CollectionChanged;
                    DrawRois();
                }
            };
            RoiCanvas.SizeChanged += (s, e) => DrawRois();
        }

        private void Rois_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) { DrawRois(); }

        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedRoi") DrawRois();
        }

        /// <summary>ROI 预览：全部框灰色描边 + 选中框主色加粗 + id 标注。</summary>
        private void DrawRois()
        {
            RoiCanvas.Children.Clear();
            if (_vm == null || _vm.Rois.Count == 0) return;
            double w = RoiCanvas.ActualWidth, h = RoiCanvas.ActualHeight;
            if (w < 10 || h < 10) return;

            foreach (var roi in _vm.Rois)
            {
                bool sel = _vm.SelectedRoi != null && ReferenceEquals(roi, _vm.SelectedRoi);
                var border = new Border
                {
                    BorderBrush = sel ? _selBrush : _dimBrush,
                    BorderThickness = new Thickness(sel ? 2.5 : 1.5),
                    CornerRadius = new CornerRadius(6),
                    Background = sel ? new SolidColorBrush(Color.FromArgb(0x22, 0xF9, 0x73, 0x16)) : Brushes.Transparent
                };
                Canvas.SetLeft(border, roi.X * w / 1000.0);
                Canvas.SetTop(border, roi.Y * h / 1000.0);
                border.Width = Math.Max(roi.W * w / 1000.0, 6);
                border.Height = Math.Max(roi.H * h / 1000.0, 6);

                var label = new TextBlock
                {
                    Text = roi.Id,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = sel ? _selBrush : _dimBrush,
                    Margin = new Thickness(4, -18, 0, 0)
                };
                border.Child = label;
                RoiCanvas.Children.Add(border);
            }
        }
    }
}
