﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using FluentNHibernate.Framework;
using Iesi.Collections;
using Iesi.Collections.Generic;
using NHibernate;

namespace FluentNHibernate.Framework
{
    public class PersistenceSpecification<T> where T : new()
    {
        private readonly List<PropertyValue> _allProperties = new List<PropertyValue>();
    	private readonly ISession _currentSession;

    	public PersistenceSpecification(ISessionSource source)
			: this(source.CreateSession())
    	{
    	}

		public PersistenceSpecification(ISession session)
		{
    		_currentSession = session;
		}

        public PersistenceSpecification<T> CheckProperty(Expression<Func<T, object>> expression, object propertyValue)
        {
            PropertyInfo property = ReflectionHelper.GetProperty(expression);
            _allProperties.Add(new PropertyValue(property, propertyValue));

            return this;
        }

        public PersistenceSpecification<T> CheckReference(Expression<Func<T, object>> expression, object propertyValue)
        {
            TransactionalSave(propertyValue);

            PropertyInfo property = ReflectionHelper.GetProperty(expression);
            _allProperties.Add(new PropertyValue(property, propertyValue));

            return this;
        }


        public PersistenceSpecification<T> CheckList<LIST>(Expression<Func<T, object>> expression,
                                                           IList<LIST> propertyValue)
        {
            foreach (LIST item in propertyValue)
            {
                TransactionalSave(item);
            }

            PropertyInfo property = ReflectionHelper.GetProperty(expression);
            _allProperties.Add(new ListValue<LIST>(property, propertyValue));

            return this;
        }

        /// <summary>
        /// Checks a list of components for validity.
        /// </summary>
        /// <typeparam name="LIST">Type of list element</typeparam>
        /// <param name="expression">Property</param>
        /// <param name="propertyValue">Value to save</param>
        public PersistenceSpecification<T> CheckComponentList<LIST>(Expression<Func<T, object>> expression, IList<LIST> propertyValue)
        {
            PropertyInfo property = ReflectionHelper.GetProperty(expression);
            _allProperties.Add(new ListValue<LIST>(property, propertyValue));

            return this;
        }

        public void VerifyTheMappings()
        {
            // Create the initial copy
            var first = new T();

            // Set the "suggested" properties, including references
            // to other entities and possibly collections
            _allProperties.ForEach(p => p.SetValue(first));

            // Save the first copy
            TransactionalSave(first);

            object firstId = _currentSession.GetIdentifier(first);

			// Clear and reset the current session
        	_currentSession.Flush();
        	_currentSession.Clear();

            // "Find" the same entity from the second IRepository
            var second = _currentSession.Get<T>(firstId);

            // Validate that each specified property and value
            // made the round trip
            // It's a bit naive right now because it fails on the first failure
            _allProperties.ForEach(p => p.CheckValue(second));
        }

        private void TransactionalSave(object propertyValue)
        {
            using (var tx = _currentSession.BeginTransaction())
            {
                _currentSession.Save(propertyValue);
                tx.Commit();
            }
        }

        #region Nested type: ListValue

        internal class ListValue<LIST> : PropertyValue
        {
			private readonly IList<LIST> _expected;

			public ListValue(PropertyInfo property, IList<LIST> propertyValue)
                : base(property, propertyValue)
            {
                _expected = propertyValue;
            }

            internal override void SetValue(object target)
            {
                try
                {
                    object collection;

                    // sorry guys - create an instance of the collection type because we can't rely
                    // on the user to pass in the correct collection type (especially if they're using
                    // an interface). I've tried to create the common ones, but I'm sure this won't be
                    // infallable.
                    if (_property.PropertyType.IsAssignableFrom(typeof(ISet<LIST>)))
                        collection = new HashedSet<LIST>(_expected);
                    else if (_property.PropertyType.IsAssignableFrom(typeof(ISet)))
                        collection = new HashedSet((ICollection)_expected);
                    else
                        collection = new List<LIST>(_expected);

                    _property.SetValue(target, collection, null);
                }
                catch (Exception e)
                {
                    string message = "Error while trying to set property " + _property.Name;
                    throw new ApplicationException(message, e);
                }
            }

            internal override void CheckValue(object target)
            {
                var actual = (IEnumerable<LIST>)_property.GetValue(target, null);
                assertGenericListMatches<LIST>(actual, _expected);
            }

			private static void assertGenericListMatches<ITEM>(IEnumerable<ITEM> actual, IEnumerable<ITEM> expected)
            {
                var actualEnumerator = actual.GetEnumerator();
                var expectedEnumerator = expected.GetEnumerator();

                int index = 0;

                while (actualEnumerator.Current != null)
                {
                    var actualValue = actualEnumerator.Current;
                    var expectedValue = expectedEnumerator.Current;

                    if (!expectedValue.Equals(actualValue))
                    {
                        string message = 
                            string.Format(
                                "Expected '{0}' but got '{1}' at position {2}", 
                                expectedValue,
                                actualValue, index);

                        throw new ApplicationException(message);
                    }

                    actualEnumerator.MoveNext();
                    expectedEnumerator.MoveNext();
                    index++;
                }
            }
        }

        #endregion

        #region Nested type: PropertyValue

        internal class PropertyValue
        {
            protected readonly PropertyInfo _property;
            protected readonly object _propertyValue;

            internal PropertyValue(PropertyInfo property, object propertyValue)
            {
                _property = property;
                _propertyValue = propertyValue;
            }

            internal virtual void SetValue(object target)
            {
                try
                {
                    _property.SetValue(target, _propertyValue, null);
                }
                catch (Exception e)
                {
                    string message = "Error while trying to set property " + _property.Name;
                    throw new ApplicationException(message, e);
                }
            }

            internal virtual void CheckValue(object target)
            {
                object actual = _property.GetValue(target, null);
                if (!_propertyValue.Equals(actual))
                {
                    string message =
                        string.Format(
                            "Expected '{0}' but got '{1}' for Property '{2}'",
                            _propertyValue,
                            actual, _property.Name);

                    throw new ApplicationException(message);
                }
            }
        }

        #endregion
    }
}