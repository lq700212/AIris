using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIris.WPF.ViewModels
{
    /// <summary>
    /// MVVM 基类（手写，不上 CommunityToolkit：空壳阶段零外部依赖、离线可还原；
    /// 口径对位 HuaJiVision ViewModelBase，后续要换 ObservableObject 可整体替换）。
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
