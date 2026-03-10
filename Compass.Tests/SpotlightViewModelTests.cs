using Compass.ViewModels;

namespace Compass.Tests;

public class ViewModelBaseTests
{
    private class TestViewModel : ViewModelBase
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private int _count;
        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
        }
    }

    [Fact]
    public void SetProperty_RaisesPropertyChanged()
    {
        var vm = new TestViewModel();
        string? changedProperty = null;
        vm.PropertyChanged += (s, e) => changedProperty = e.PropertyName;

        vm.Name = "test";

        Assert.Equal("Name", changedProperty);
    }

    [Fact]
    public void SetProperty_DoesNotRaiseWhenValueUnchanged()
    {
        var vm = new TestViewModel();
        vm.Name = "test";

        bool raised = false;
        vm.PropertyChanged += (s, e) => raised = true;

        vm.Name = "test"; // same value
        Assert.False(raised);
    }

    [Fact]
    public void SetProperty_UpdatesValue()
    {
        var vm = new TestViewModel();
        vm.Name = "hello";
        Assert.Equal("hello", vm.Name);
    }

    [Fact]
    public void SetProperty_WorksWithValueTypes()
    {
        var vm = new TestViewModel();
        vm.Count = 42;
        Assert.Equal(42, vm.Count);
    }
}
