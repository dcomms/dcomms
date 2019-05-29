
using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Dcomms.SUBT.GUI
{     
	public abstract class BaseNotify : INotifyPropertyChanged
	{
		/// <summary>
		/// Raised when a property on this object has a new value
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;        	

		/// <summary>
		/// Raises this object's PropertyChanged event.
		/// </summary>
		/// <param name="propertyName">The property that has a new value.</param>
        public virtual void RaisePropertyChanged(string propertyName)
		{
			var handler = PropertyChanged;
			if (handler != null)
			{
				handler(this, new PropertyChangedEventArgs(propertyName));
			}
		 }


		/// <summary>
		/// Raises this object's PropertyChanged event.
		/// </summary>
		/// <typeparam name="T">The type of the property that has a new value</typeparam>
		/// <param name="propertyExpression">A Lambda expression representing the property that has a new value.</param>
		public void RaisePropertyChanged<T>(Expression<Func<T>> propertyExpression)
		{
			var propertyName = ExtractPropertyName(propertyExpression);
			RaisePropertyChanged(propertyName);
		}

		protected static string ExtractPropertyName<T>(Expression<Func<T>> propertyExpression)
		{
			if (propertyExpression == null)
			{
				throw new ArgumentNullException("propertyExpression");
			}

			var memberExpression = propertyExpression.Body as MemberExpression;
			if (memberExpression == null)
			{
				throw new ArgumentException();
			}

			var property = memberExpression.Member as PropertyInfo;
			if (property == null)
			{
				throw new ArgumentException();
			}

			var getMethod = property.GetGetMethod(true);

			if (getMethod == null)
			{
				// this shouldn't happen - the expression would reject the property before reaching this far
				throw new ArgumentException();
			}

			if (getMethod.IsStatic)
			{
				throw new ArgumentException();
			}

			return memberExpression.Member.Name;
		}
	}
	
}
