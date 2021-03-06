// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Reflection.Runtime.General;

namespace System.Reflection.Runtime.ParameterInfos
{
    //
    // This implements ParameterInfo objects owned by MethodBase objects that have associated Parameter metadata. (In practice,
    // this means all non-return parameters since most such parameters have at least a name.)
    //
    internal abstract class RuntimeFatMethodParameterInfo : RuntimeMethodParameterInfo
    {
        protected RuntimeFatMethodParameterInfo(MethodBase member, int position, QSignatureTypeHandle qualifiedParameterTypeHandle, TypeContext typeContext)
            : base(member, position, qualifiedParameterTypeHandle, typeContext)
        {
        }

        public sealed override bool HasDefaultValue => DefaultValueInfo.Item1;
        public sealed override object DefaultValue => DefaultValueInfo.Item2;

        public sealed override object RawDefaultValue
        {
            get
            {
                Tuple<object> rawDefaultValueInfo = _lazyRawDefaultValueInfo;
                if (rawDefaultValueInfo == null)
                {
                    object rawDefaultValue;
                    bool dontCare = GetDefaultValueOrSentinel(raw: true, defaultValue: out rawDefaultValue);
                    rawDefaultValueInfo = _lazyRawDefaultValueInfo = Tuple.Create(rawDefaultValue);
                }
                return rawDefaultValueInfo.Item1;
            }
        }

        protected abstract bool GetDefaultValueIfAvailable(bool raw, out object defaultValue);

        private Tuple<bool, object> DefaultValueInfo
        {
            get
            {
                Tuple<bool, object> defaultValueInfo = _lazyDefaultValueInfo;
                if (defaultValueInfo == null)
                {
                    object defaultValue;
                    bool hasDefaultValue = GetDefaultValueOrSentinel(raw: false, defaultValue: out defaultValue);
                    defaultValueInfo = _lazyDefaultValueInfo = Tuple.Create(hasDefaultValue, defaultValue);
                }
                return defaultValueInfo;
            }
        }

        private bool GetDefaultValueOrSentinel(bool raw, out object defaultValue)
        {
            bool hasDefaultValue = GetDefaultValueIfAvailable(raw, out defaultValue);
            if (!hasDefaultValue)
            {
                defaultValue = IsOptional ? (object)Missing.Value : (object)DBNull.Value;
            }
            return hasDefaultValue;
        }

        private volatile Tuple<bool, object> _lazyDefaultValueInfo;
        private volatile Tuple<object> _lazyRawDefaultValueInfo;
    }
}

