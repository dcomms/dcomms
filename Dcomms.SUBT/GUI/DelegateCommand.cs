using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace Dcomms.SUBT.GUI
{
	public class DelegateCommand : ICommand 
	{
		readonly Action action;
		public DelegateCommand(Action action)
		{
			if (action == null) throw new ArgumentNullException();
			this.action = action;
		}
		public event EventHandler CanExecuteChanged;
		public bool CanExecute(object parameter)
		{
			return true;
		}
		public void Execute(object parameter)
		{
			action();
		}
	}
    
    public class DelegateCommandWithParameter : ICommand
    {
        readonly Action<object> action;
        public DelegateCommandWithParameter(Action<object> action)
        {
            if (action == null) throw new ArgumentNullException();
            this.action = action;
        }
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter)
        {
            return true;
        }
        public void Execute(object parameter)
        {
            action(parameter);
        }
    }
}
