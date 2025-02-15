﻿using System;

namespace Umbraco.Cms.Core.Events
{
    /// <summary>
    /// This is used to know if the event arg attributed should supersede another event arg type when
    /// tracking events for the same entity. If one event args supersedes another then the event args that have been superseded
    /// will mean that the event will not be dispatched or the args will be filtered to exclude the entity.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class SupersedeEventAttribute : Attribute
    {
        public Type SupersededEventArgsType { get; private set; }

        public SupersedeEventAttribute(Type supersededEventArgsType)
        {
            SupersededEventArgsType = supersededEventArgsType;
        }
    }
}
