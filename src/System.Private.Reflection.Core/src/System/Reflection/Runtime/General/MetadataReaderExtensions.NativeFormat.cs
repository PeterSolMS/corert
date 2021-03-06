// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Runtime.Assemblies;

using Internal.LowLevelLinq;
using Internal.Reflection.Core;

using Internal.Runtime.Augments;

using Internal.Metadata.NativeFormat;
using NativeFormatAssemblyFlags = global::Internal.Metadata.NativeFormat.AssemblyFlags;

namespace System.Reflection.Runtime.General
{
    //
    // Collect various metadata reading tasks for better chunking...
    //
    internal static class NativeFormatMetadataReaderExtensions
    {
        public static bool StringOrNullEquals(this ConstantStringValueHandle handle, String valueOrNull, MetadataReader reader)
        {
            if (valueOrNull == null)
                return handle.IsNull(reader);
            if (handle.IsNull(reader))
                return false;
            return handle.StringEquals(valueOrNull, reader);
        }

        // Needed for RuntimeMappingTable access
        public static int AsInt(this TypeDefinitionHandle typeDefinitionHandle)
        {
            unsafe
            {
                return *(int*)&typeDefinitionHandle;
            }
        }

        public static TypeDefinitionHandle AsTypeDefinitionHandle(this int i)
        {
            unsafe
            {
                return *(TypeDefinitionHandle*)&i;
            }
        }

        public static int AsInt(this MethodHandle methodHandle)
        {
            unsafe
            {
                return *(int*)&methodHandle;
            }
        }

        public static MethodHandle AsMethodHandle(this int i)
        {
            unsafe
            {
                return *(MethodHandle*)&i;
            }
        }

        public static int AsInt(this FieldHandle fieldHandle)
        {
            unsafe
            {
                return *(int*)&fieldHandle;
            }
        }

        public static FieldHandle AsFieldHandle(this int i)
        {
            unsafe
            {
                return *(FieldHandle*)&i;
            }
        }


        public static bool IsNamespaceDefinitionHandle(this Handle handle, MetadataReader reader)
        {
            HandleType handleType = handle.HandleType;
            return handleType == HandleType.NamespaceDefinition;
        }

        public static bool IsNamespaceReferenceHandle(this Handle handle, MetadataReader reader)
        {
            HandleType handleType = handle.HandleType;
            return handleType == HandleType.NamespaceReference;
        }

        // Conversion where a invalid handle type indicates bad metadata rather a mistake by the caller.
        public static ScopeReferenceHandle ToExpectedScopeReferenceHandle(this Handle handle, MetadataReader reader)
        {
            try
            {
                return handle.ToScopeReferenceHandle(reader);
            }
            catch (ArgumentException)
            {
                throw new BadImageFormatException();
            }
        }

        // Conversion where a invalid handle type indicates bad metadata rather a mistake by the caller.
        public static NamespaceReferenceHandle ToExpectedNamespaceReferenceHandle(this Handle handle, MetadataReader reader)
        {
            try
            {
                return handle.ToNamespaceReferenceHandle(reader);
            }
            catch (ArgumentException)
            {
                throw new BadImageFormatException();
            }
        }

        // Conversion where a invalid handle type indicates bad metadata rather a mistake by the caller.
        public static TypeDefinitionHandle ToExpectedTypeDefinitionHandle(this Handle handle, MetadataReader reader)
        {
            try
            {
                return handle.ToTypeDefinitionHandle(reader);
            }
            catch (ArgumentException)
            {
                throw new BadImageFormatException();
            }
        }

        // Return any custom modifiers modifying the passed-in type and whose required/optional bit matches the passed in boolean.
        // Because this is intended to service the GetCustomModifiers() apis, this helper will always return a freshly allocated array
        // safe for returning to api callers.
        public static Type[] GetCustomModifiers(this Handle handle, MetadataReader reader, TypeContext typeContext, bool optional)
        {
            HandleType handleType = handle.HandleType;
            Debug.Assert(handleType == HandleType.TypeDefinition || handleType == HandleType.TypeReference || handleType == HandleType.TypeSpecification || handleType == HandleType.ModifiedType);
            if (handleType != HandleType.ModifiedType)
                return Array.Empty<Type>();

            LowLevelList<Type> customModifiers = new LowLevelList<Type>();
            do
            {
                ModifiedType modifiedType = handle.ToModifiedTypeHandle(reader).GetModifiedType(reader);
                if (optional == modifiedType.IsOptional)
                {
                    Type customModifier = modifiedType.ModifierType.Resolve(reader, typeContext);
                    customModifiers.Add(customModifier);
                }

                handle = modifiedType.Type;
                handleType = handle.HandleType;
            }
            while (handleType == HandleType.ModifiedType);
            return customModifiers.ToArray();
        }

        public static MethodSignature ParseMethodSignature(this Handle handle, MetadataReader reader)
        {
            return handle.ToMethodSignatureHandle(reader).GetMethodSignature(reader);
        }

        public static FieldSignature ParseFieldSignature(this Handle handle, MetadataReader reader)
        {
            return handle.ToFieldSignatureHandle(reader).GetFieldSignature(reader);
        }

        public static PropertySignature ParsePropertySignature(this Handle handle, MetadataReader reader)
        {
            return handle.ToPropertySignatureHandle(reader).GetPropertySignature(reader);
        }

        //
        // Used to split methods between DeclaredMethods and DeclaredConstructors.
        //
        public static bool IsConstructor(this MethodHandle methodHandle, MetadataReader reader)
        {
            Method method = methodHandle.GetMethod(reader);
            return IsConstructor(ref method, reader);
        }

        // This is specially designed for a hot path so we make some compromises in the signature:
        //
        //     - "method" is passed by reference even though no side-effects are intended.
        //
        public static bool IsConstructor(ref Method method, MetadataReader reader)
        {
            if ((method.Flags & (MethodAttributes.RTSpecialName | MethodAttributes.SpecialName)) != (MethodAttributes.RTSpecialName | MethodAttributes.SpecialName))
                return false;

            ConstantStringValueHandle nameHandle = method.Name;
            return nameHandle.StringEquals(ConstructorInfo.ConstructorName, reader) || nameHandle.StringEquals(ConstructorInfo.TypeConstructorName, reader);
        }

        private static Exception ParseBoxedEnumConstantValue(this ConstantBoxedEnumValueHandle handle, MetadataReader reader, out Object value)
        {
            ConstantBoxedEnumValue record = handle.GetConstantBoxedEnumValue(reader);

            Exception exception = null;
            Type enumType = record.Type.TryResolve(reader, new TypeContext(null, null), ref exception);
            if (enumType == null)
            {
                value = null;
                return exception;
            }

            if (!enumType.IsEnum)
                throw new BadImageFormatException();

            Type underlyingType = Enum.GetUnderlyingType(enumType);

            // Now box the value as the specified enum type.
            unsafe
            {
                switch (record.Value.HandleType)
                {
                    case HandleType.ConstantByteValue:
                        {
                            if (underlyingType != CommonRuntimeTypes.Byte)
                                throw new BadImageFormatException();

                            byte v = record.Value.ToConstantByteValueHandle(reader).GetConstantByteValue(reader).Value;
                            value = RuntimeAugments.Box(enumType.TypeHandle, (IntPtr)(&v));
                            return null;
                        }
                    case HandleType.ConstantSByteValue:
                        {
                            if (underlyingType != CommonRuntimeTypes.SByte)
                                throw new BadImageFormatException();

                            sbyte v = record.Value.ToConstantSByteValueHandle(reader).GetConstantSByteValue(reader).Value;
                            value = RuntimeAugments.Box(enumType.TypeHandle, (IntPtr)(&v));
                            return null;
                        }
                    case HandleType.ConstantInt16Value:
                        {
                            if (underlyingType != CommonRuntimeTypes.Int16)
                                throw new BadImageFormatException();

                            short v = record.Value.ToConstantInt16ValueHandle(reader).GetConstantInt16Value(reader).Value;
                            value = RuntimeAugments.Box(enumType.TypeHandle, (IntPtr)(&v));
                            return null;
                        }
                    case HandleType.ConstantUInt16Value:
                        {
                            if (underlyingType != CommonRuntimeTypes.UInt16)
                                throw new BadImageFormatException();

                            ushort v = record.Value.ToConstantUInt16ValueHandle(reader).GetConstantUInt16Value(reader).Value;
                            value = RuntimeAugments.Box(enumType.TypeHandle, (IntPtr)(&v));
                            return null;
                        }
                    case HandleType.ConstantInt32Value:
                        {
                            if (underlyingType != CommonRuntimeTypes.Int32)
                                throw new BadImageFormatException();

                            int v = record.Value.ToConstantInt32ValueHandle(reader).GetConstantInt32Value(reader).Value;
                            value = RuntimeAugments.Box(enumType.TypeHandle, (IntPtr)(&v));
                            return null;
                        }
                    case HandleType.ConstantUInt32Value:
                        {
                            if (underlyingType != CommonRuntimeTypes.UInt32)
                                throw new BadImageFormatException();

                            uint v = record.Value.ToConstantUInt32ValueHandle(reader).GetConstantUInt32Value(reader).Value;
                            value = RuntimeAugments.Box(enumType.TypeHandle, (IntPtr)(&v));
                            return null;
                        }
                    case HandleType.ConstantInt64Value:
                        {
                            if (underlyingType != CommonRuntimeTypes.Int64)
                                throw new BadImageFormatException();

                            long v = record.Value.ToConstantInt64ValueHandle(reader).GetConstantInt64Value(reader).Value;
                            value = RuntimeAugments.Box(enumType.TypeHandle, (IntPtr)(&v));
                            return null;
                        }
                    case HandleType.ConstantUInt64Value:
                        {
                            if (underlyingType != CommonRuntimeTypes.UInt64)
                                throw new BadImageFormatException();

                            ulong v = record.Value.ToConstantUInt64ValueHandle(reader).GetConstantUInt64Value(reader).Value;
                            value = RuntimeAugments.Box(enumType.TypeHandle, (IntPtr)(&v));
                            return null;
                        }
                    default:
                        throw new BadImageFormatException();
                }
            }
        }

        public static Object ParseConstantValue(this Handle handle, MetadataReader reader)
        {
            Object value;
            Exception exception = handle.TryParseConstantValue(reader, out value);
            if (exception != null)
                throw exception;
            return value;
        }

        public static Exception TryParseConstantValue(this Handle handle, MetadataReader reader, out Object value)
        {
            HandleType handleType = handle.HandleType;
            switch (handleType)
            {
                case HandleType.ConstantBooleanValue:
                    value = handle.ToConstantBooleanValueHandle(reader).GetConstantBooleanValue(reader).Value;
                    return null;
                case HandleType.ConstantStringValue:
                    value = handle.ToConstantStringValueHandle(reader).GetConstantStringValue(reader).Value;
                    return null;
                case HandleType.ConstantCharValue:
                    value = handle.ToConstantCharValueHandle(reader).GetConstantCharValue(reader).Value;
                    return null;
                case HandleType.ConstantByteValue:
                    value = handle.ToConstantByteValueHandle(reader).GetConstantByteValue(reader).Value;
                    return null;
                case HandleType.ConstantSByteValue:
                    value = handle.ToConstantSByteValueHandle(reader).GetConstantSByteValue(reader).Value;
                    return null;
                case HandleType.ConstantInt16Value:
                    value = handle.ToConstantInt16ValueHandle(reader).GetConstantInt16Value(reader).Value;
                    return null;
                case HandleType.ConstantUInt16Value:
                    value = handle.ToConstantUInt16ValueHandle(reader).GetConstantUInt16Value(reader).Value;
                    return null;
                case HandleType.ConstantInt32Value:
                    value = handle.ToConstantInt32ValueHandle(reader).GetConstantInt32Value(reader).Value;
                    return null;
                case HandleType.ConstantUInt32Value:
                    value = handle.ToConstantUInt32ValueHandle(reader).GetConstantUInt32Value(reader).Value;
                    return null;
                case HandleType.ConstantInt64Value:
                    value = handle.ToConstantInt64ValueHandle(reader).GetConstantInt64Value(reader).Value;
                    return null;
                case HandleType.ConstantUInt64Value:
                    value = handle.ToConstantUInt64ValueHandle(reader).GetConstantUInt64Value(reader).Value;
                    return null;
                case HandleType.ConstantSingleValue:
                    value = handle.ToConstantSingleValueHandle(reader).GetConstantSingleValue(reader).Value;
                    return null;
                case HandleType.ConstantDoubleValue:
                    value = handle.ToConstantDoubleValueHandle(reader).GetConstantDoubleValue(reader).Value;
                    return null;
                case HandleType.TypeDefinition:
                case HandleType.TypeReference:
                case HandleType.TypeSpecification:
                    {
                        Exception exception = null;
                        Type type = handle.TryResolve(reader, new TypeContext(null, null), ref exception);
                        value = type;
                        return (value == null) ? exception : null;
                    }
                case HandleType.ConstantReferenceValue:
                    value = null;
                    return null;
                case HandleType.ConstantBoxedEnumValue:
                    {
                        return handle.ToConstantBoxedEnumValueHandle(reader).ParseBoxedEnumConstantValue(reader, out value);
                    }
                default:
                    {
                        Exception exception;
                        value = handle.TryParseConstantArray(reader, out exception);
                        if (value == null)
                            return exception;
                        return null;
                    }
            }
        }

        private static Array TryParseConstantArray(this Handle handle, MetadataReader reader, out Exception exception)
        {
            exception = null;

            HandleType handleType = handle.HandleType;
            switch (handleType)
            {
                case HandleType.ConstantBooleanArray:
                    return handle.ToConstantBooleanArrayHandle(reader).GetConstantBooleanArray(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantCharArray:
                    return handle.ToConstantCharArrayHandle(reader).GetConstantCharArray(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantByteArray:
                    return handle.ToConstantByteArrayHandle(reader).GetConstantByteArray(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantSByteArray:
                    return handle.ToConstantSByteArrayHandle(reader).GetConstantSByteArray(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantInt16Array:
                    return handle.ToConstantInt16ArrayHandle(reader).GetConstantInt16Array(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantUInt16Array:
                    return handle.ToConstantUInt16ArrayHandle(reader).GetConstantUInt16Array(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantInt32Array:
                    return handle.ToConstantInt32ArrayHandle(reader).GetConstantInt32Array(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantUInt32Array:
                    return handle.ToConstantUInt32ArrayHandle(reader).GetConstantUInt32Array(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantInt64Array:
                    return handle.ToConstantInt64ArrayHandle(reader).GetConstantInt64Array(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantUInt64Array:
                    return handle.ToConstantUInt64ArrayHandle(reader).GetConstantUInt64Array(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantSingleArray:
                    return handle.ToConstantSingleArrayHandle(reader).GetConstantSingleArray(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantDoubleArray:
                    return handle.ToConstantDoubleArrayHandle(reader).GetConstantDoubleArray(reader).Value.ReadOnlyCollectionToArray();

                case HandleType.ConstantEnumArray:
                    return TryParseConstantEnumArray(handle.ToConstantEnumArrayHandle(reader), reader, out exception);

                case HandleType.ConstantStringArray:
                    {
                        Handle[] constantHandles = handle.ToConstantStringArrayHandle(reader).GetConstantStringArray(reader).Value.ToArray();
                        string[] elements = new string[constantHandles.Length];
                        for (int i = 0; i < constantHandles.Length; i++)
                        {
                            object elementValue;
                            exception = constantHandles[i].TryParseConstantValue(reader, out elementValue);
                            if (exception != null)
                                return null;
                            elements[i] = (string)elementValue;
                        }
                        return elements;
                    }

                case HandleType.ConstantHandleArray:
                    {
                        Handle[] constantHandles = handle.ToConstantHandleArrayHandle(reader).GetConstantHandleArray(reader).Value.ToArray();
                        object[] elements = new object[constantHandles.Length];
                        for (int i = 0; i < constantHandles.Length; i++)
                        {
                            exception = constantHandles[i].TryParseConstantValue(reader, out elements[i]);
                            if (exception != null)
                                return null;
                        }
                        return elements;
                    }
                default:
                    throw new BadImageFormatException();
            }
        }

        private static Array TryParseConstantEnumArray(this ConstantEnumArrayHandle handle, MetadataReader reader, out Exception exception)
        {
            exception = null;

            ConstantEnumArray enumArray = handle.GetConstantEnumArray(reader);
            Type elementType = enumArray.ElementType.TryResolve(reader, new TypeContext(null, null), ref exception);
            if (exception != null)
                return null;

            switch (enumArray.Value.HandleType)
            {
                case HandleType.ConstantByteArray:
                    return enumArray.Value.ToConstantByteArrayHandle(reader).GetConstantByteArray(reader).Value.ReadOnlyCollectionToEnumArray(elementType);

                case HandleType.ConstantSByteArray:
                    return enumArray.Value.ToConstantSByteArrayHandle(reader).GetConstantSByteArray(reader).Value.ReadOnlyCollectionToEnumArray(elementType);

                case HandleType.ConstantInt16Array:
                    return enumArray.Value.ToConstantInt16ArrayHandle(reader).GetConstantInt16Array(reader).Value.ReadOnlyCollectionToEnumArray(elementType);

                case HandleType.ConstantUInt16Array:
                    return enumArray.Value.ToConstantUInt16ArrayHandle(reader).GetConstantUInt16Array(reader).Value.ReadOnlyCollectionToEnumArray(elementType);

                case HandleType.ConstantInt32Array:
                    return enumArray.Value.ToConstantInt32ArrayHandle(reader).GetConstantInt32Array(reader).Value.ReadOnlyCollectionToEnumArray(elementType);

                case HandleType.ConstantUInt32Array:
                    return enumArray.Value.ToConstantUInt32ArrayHandle(reader).GetConstantUInt32Array(reader).Value.ReadOnlyCollectionToEnumArray(elementType);

                case HandleType.ConstantInt64Array:
                    return enumArray.Value.ToConstantInt64ArrayHandle(reader).GetConstantInt64Array(reader).Value.ReadOnlyCollectionToEnumArray(elementType);

                case HandleType.ConstantUInt64Array:
                    return enumArray.Value.ToConstantUInt64ArrayHandle(reader).GetConstantUInt64Array(reader).Value.ReadOnlyCollectionToEnumArray(elementType);

                default:
                    throw new BadImageFormatException();
            }
        }

        public static Handle GetAttributeTypeHandle(this CustomAttribute customAttribute,
                                                    MetadataReader reader)
        {
            HandleType constructorHandleType = customAttribute.Constructor.HandleType;

            if (constructorHandleType == HandleType.QualifiedMethod)
                return customAttribute.Constructor.ToQualifiedMethodHandle(reader).GetQualifiedMethod(reader).EnclosingType;
            else if (constructorHandleType == HandleType.MemberReference)
                return customAttribute.Constructor.ToMemberReferenceHandle(reader).GetMemberReference(reader).Parent;
            else
                throw new BadImageFormatException();
        }

        //
        // Lightweight check to see if a custom attribute's is of a well-known type.
        //
        // This check performs without instantating the Type object and bloating memory usage. On the flip side,
        // it doesn't check on whether the type is defined in a paricular assembly. The desktop CLR typically doesn't
        // check this either so this is useful from a compat persective as well.
        //
        public static bool IsCustomAttributeOfType(this CustomAttributeHandle customAttributeHandle,
                                                   MetadataReader reader,
                                                   String ns,
                                                   String name)
        {
            String[] namespaceParts = ns.Split('.');
            Handle typeHandle = customAttributeHandle.GetCustomAttribute(reader).GetAttributeTypeHandle(reader);
            HandleType handleType = typeHandle.HandleType;
            if (handleType == HandleType.TypeDefinition)
            {
                TypeDefinition typeDefinition = typeHandle.ToTypeDefinitionHandle(reader).GetTypeDefinition(reader);
                if (!typeDefinition.Name.StringEquals(name, reader))
                    return false;
                NamespaceDefinitionHandle nsHandle = typeDefinition.NamespaceDefinition;
                int idx = namespaceParts.Length;
                while (idx-- != 0)
                {
                    String namespacePart = namespaceParts[idx];
                    NamespaceDefinition namespaceDefinition = nsHandle.GetNamespaceDefinition(reader);
                    if (!namespaceDefinition.Name.StringOrNullEquals(namespacePart, reader))
                        return false;
                    if (!namespaceDefinition.ParentScopeOrNamespace.IsNamespaceDefinitionHandle(reader))
                        return false;
                    nsHandle = namespaceDefinition.ParentScopeOrNamespace.ToNamespaceDefinitionHandle(reader);
                }
                if (!nsHandle.GetNamespaceDefinition(reader).Name.StringOrNullEquals(null, reader))
                    return false;
                return true;
            }
            else if (handleType == HandleType.TypeReference)
            {
                TypeReference typeReference = typeHandle.ToTypeReferenceHandle(reader).GetTypeReference(reader);
                if (!typeReference.TypeName.StringEquals(name, reader))
                    return false;
                if (!typeReference.ParentNamespaceOrType.IsNamespaceReferenceHandle(reader))
                    return false;
                NamespaceReferenceHandle nsHandle = typeReference.ParentNamespaceOrType.ToNamespaceReferenceHandle(reader);
                int idx = namespaceParts.Length;
                while (idx-- != 0)
                {
                    String namespacePart = namespaceParts[idx];
                    NamespaceReference namespaceReference = nsHandle.GetNamespaceReference(reader);
                    if (!namespaceReference.Name.StringOrNullEquals(namespacePart, reader))
                        return false;
                    if (!namespaceReference.ParentScopeOrNamespace.IsNamespaceReferenceHandle(reader))
                        return false;
                    nsHandle = namespaceReference.ParentScopeOrNamespace.ToNamespaceReferenceHandle(reader);
                }
                if (!nsHandle.GetNamespaceReference(reader).Name.StringOrNullEquals(null, reader))
                    return false;
                return true;
            }
            else
                throw new NotSupportedException();
        }


        public static String ToNamespaceName(this NamespaceDefinitionHandle namespaceDefinitionHandle, MetadataReader reader)
        {
            String ns = "";
            for (;;)
            {
                NamespaceDefinition currentNamespaceDefinition = namespaceDefinitionHandle.GetNamespaceDefinition(reader);
                String name = currentNamespaceDefinition.Name.GetStringOrNull(reader);
                if (name != null)
                {
                    if (ns.Length != 0)
                        ns = "." + ns;
                    ns = name + ns;
                }
                Handle nextHandle = currentNamespaceDefinition.ParentScopeOrNamespace;
                HandleType handleType = nextHandle.HandleType;
                if (handleType == HandleType.ScopeDefinition)
                    break;
                if (handleType == HandleType.NamespaceDefinition)
                {
                    namespaceDefinitionHandle = nextHandle.ToNamespaceDefinitionHandle(reader);
                    continue;
                }

                throw new BadImageFormatException(SR.Bif_InvalidMetadata);
            }
            return ns;
        }

        public static IEnumerable<NamespaceDefinitionHandle> GetTransitiveNamespaces(this MetadataReader reader, IEnumerable<NamespaceDefinitionHandle> namespaceHandles)
        {
            foreach (NamespaceDefinitionHandle namespaceHandle in namespaceHandles)
            {
                yield return namespaceHandle;

                NamespaceDefinition namespaceDefinition = namespaceHandle.GetNamespaceDefinition(reader);
                foreach (NamespaceDefinitionHandle childNamespaceHandle in GetTransitiveNamespaces(reader, namespaceDefinition.NamespaceDefinitions))
                    yield return childNamespaceHandle;
            }
        }

        public static IEnumerable<TypeDefinitionHandle> GetTopLevelTypes(this MetadataReader reader, IEnumerable<NamespaceDefinitionHandle> namespaceHandles)
        {
            foreach (NamespaceDefinitionHandle namespaceHandle in namespaceHandles)
            {
                NamespaceDefinition namespaceDefinition = namespaceHandle.GetNamespaceDefinition(reader);
                foreach (TypeDefinitionHandle typeDefinitionHandle in namespaceDefinition.TypeDefinitions)
                {
                    yield return typeDefinitionHandle;
                }
            }
        }

        public static IEnumerable<TypeDefinitionHandle> GetTransitiveTypes(this MetadataReader reader, IEnumerable<TypeDefinitionHandle> typeDefinitionHandles, bool publicOnly)
        {
            foreach (TypeDefinitionHandle typeDefinitionHandle in typeDefinitionHandles)
            {
                TypeDefinition typeDefinition = typeDefinitionHandle.GetTypeDefinition(reader);

                if (publicOnly)
                {
                    TypeAttributes visibility = typeDefinition.Flags & TypeAttributes.VisibilityMask;
                    if (visibility != TypeAttributes.Public && visibility != TypeAttributes.NestedPublic)
                        continue;
                }

                yield return typeDefinitionHandle;

                foreach (TypeDefinitionHandle nestedTypeDefinitionHandle in GetTransitiveTypes(reader, typeDefinition.NestedTypes, publicOnly))
                    yield return nestedTypeDefinitionHandle;
            }
        }

        /// <summary>
        /// Reverse len characters in a StringBuilder starting at offset index
        /// </summary>
        private static void ReverseStringInStringBuilder(StringBuilder builder, int index, int len)
        {
            int back = index + len - 1;
            int front = index;
            while (front < back)
            {
                char temp = builder[front];
                builder[front] = builder[back];
                builder[back] = temp;
                front++;
                back--;
            }
        }
        
        public static string ToFullyQualifiedTypeName(this NamespaceReferenceHandle namespaceReferenceHandle, string typeName, MetadataReader reader)
        {
            StringBuilder fullName = new StringBuilder(64);
            NamespaceReference namespaceReference;
            for (;;)
            {
                namespaceReference = namespaceReferenceHandle.GetNamespaceReference(reader);
                String namespacePart = namespaceReference.Name.GetStringOrNull(reader);
                if (namespacePart == null)
                    break;
                fullName.Append('.');
                int index = fullName.Length;
                fullName.Append(namespacePart);
                ReverseStringInStringBuilder(fullName, index, namespacePart.Length);
                namespaceReferenceHandle = namespaceReference.ParentScopeOrNamespace.ToExpectedNamespaceReferenceHandle(reader);
            }
            ReverseStringInStringBuilder(fullName, 0, fullName.Length);
            fullName.Append(typeName);
            return fullName.ToString();
        }
    }
}

