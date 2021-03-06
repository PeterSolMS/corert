// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Internal.NativeFormat;

namespace Internal.TypeSystem
{
    [Flags]
    public enum MethodSignatureFlags
    {
        None = 0x0000,
        // TODO: Generic, etc.

        UnmanagedCallingConventionMask       = 0x000F,
        UnmanagedCallingConventionCdecl      = 0x0001,
        UnmanagedCallingConventionStdCall    = 0x0002,
        UnmanagedCallingConventionThisCall   = 0x0003,

        Static = 0x0010,
    }

    /// <summary>
    /// Represents the parameter types, the return type, and flags of a method.
    /// </summary>
    public sealed partial class MethodSignature
    {
        internal MethodSignatureFlags _flags;
        internal int _genericParameterCount;
        internal TypeDesc _returnType;
        internal TypeDesc[] _parameters;

        public MethodSignature(MethodSignatureFlags flags, int genericParameterCount, TypeDesc returnType, TypeDesc[] parameters)
        {
            _flags = flags;
            _genericParameterCount = genericParameterCount;
            _returnType = returnType;
            _parameters = parameters;

            Debug.Assert(parameters != null, "Parameters must not be null");
        }

        public MethodSignatureFlags Flags
        {
            get
            {
                return _flags;
            }
        }

        public bool IsStatic
        {
            get
            {
                return (_flags & MethodSignatureFlags.Static) != 0;
            }
        }

        public int GenericParameterCount
        {
            get
            {
                return _genericParameterCount;
            }
        }

        public TypeDesc ReturnType
        {
            get
            {
                return _returnType;
            }
        }

        /// <summary>
        /// Gets the parameter type at the specified index.
        /// </summary>
        [IndexerName("Parameter")]
        public TypeDesc this[int index]
        {
            get
            {
                return _parameters[index];
            }
        }

        /// <summary>
        /// Gets the number of parameters of this method signature.
        /// </summary>
        public int Length
        {
            get
            {
                return _parameters.Length;
            }
        }

        public bool Equals(MethodSignature otherSignature)
        {
            // TODO: Generics, etc.
            if (this._flags != otherSignature._flags)
                return false;

            if (this._genericParameterCount != otherSignature._genericParameterCount)
                return false;

            if (this._returnType != otherSignature._returnType)
                return false;

            if (this._parameters.Length != otherSignature._parameters.Length)
                return false;

            for (int i = 0; i < this._parameters.Length; i++)
            {
                if (this._parameters[i] != otherSignature._parameters[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is MethodSignature && Equals((MethodSignature)obj);
        }

        public override int GetHashCode()
        {
            return TypeHashingAlgorithms.ComputeMethodSignatureHashCode(_returnType.GetHashCode(), _parameters);
        }
    }

    /// <summary>
    /// Helper structure for building method signatures by cloning an existing method signature.
    /// </summary>
    /// <remarks>
    /// This can potentially avoid array allocation costs for allocating the parameter type list.
    /// </remarks>
    public struct MethodSignatureBuilder
    {
        private MethodSignature _template;
        private MethodSignatureFlags _flags;
        private int _genericParameterCount;
        private TypeDesc _returnType;
        private TypeDesc[] _parameters;

        public MethodSignatureBuilder(MethodSignature template)
        {
            _template = template;

            _flags = template._flags;
            _genericParameterCount = template._genericParameterCount;
            _returnType = template._returnType;
            _parameters = template._parameters;
        }

        public MethodSignatureFlags Flags
        {
            set
            {
                _flags = value;
            }
        }

        public TypeDesc ReturnType
        {
            set
            {
                _returnType = value;
            }
        }

        [System.Runtime.CompilerServices.IndexerName("Parameter")]
        public TypeDesc this[int index]
        {
            set
            {
                if (_parameters[index] == value)
                    return;

                if (_template != null && _parameters == _template._parameters)
                {
                    TypeDesc[] parameters = new TypeDesc[_parameters.Length];
                    for (int i = 0; i < parameters.Length; i++)
                        parameters[i] = _parameters[i];
                    _parameters = parameters;
                }
                _parameters[index] = value;
            }
        }

        public int Length
        {
            set
            {
                _parameters = new TypeDesc[value];
                _template = null;
            }
        }

        public MethodSignature ToSignature()
        {
            if (_template == null ||
                _flags != _template._flags ||
                _genericParameterCount != _template._genericParameterCount ||
                _returnType != _template._returnType ||
                _parameters != _template._parameters)
            {
                _template = new MethodSignature(_flags, _genericParameterCount, _returnType, _parameters);
            }

            return _template;
        }
    }

    /// <summary>
    /// Represents the fundamental base type for all methods within the type system.
    /// </summary>
    public abstract partial class MethodDesc : TypeSystemEntity
    {
        public static readonly MethodDesc[] EmptyMethods = new MethodDesc[0];

        private int _hashcode;

        /// <summary>
        /// Allows a performance optimization that skips the potentially expensive
        /// construction of a hash code if a hash code has already been computed elsewhere.
        /// Use to allow objects to have their hashcode computed
        /// independently of the allocation of a MethodDesc object
        /// For instance, compute the hashcode when looking up the object,
        /// then when creating the object, pass in the hashcode directly.
        /// The hashcode specified MUST exactly match the algorithm implemented
        /// on this type normally.
        /// </summary>
        protected void SetHashCode(int hashcode)
        {
            _hashcode = hashcode;
            Debug.Assert(hashcode == ComputeHashCode());
        }

        public sealed override int GetHashCode()
        {
            if (_hashcode != 0)
                return _hashcode;

            return AcquireHashCode();
        }

        private int AcquireHashCode()
        {
            _hashcode = ComputeHashCode();
            return _hashcode;
        }

        /// <summary>
        /// Compute HashCode. Should only be overriden by a MethodDesc that represents an instantiated method.
        /// </summary>
        protected virtual int ComputeHashCode()
        {
            return TypeHashingAlgorithms.ComputeMethodHashCode(OwningType.GetHashCode(), TypeHashingAlgorithms.ComputeNameHashCode(Name));
        }

        public override bool Equals(Object o)
        {
            // Its only valid to compare two MethodDescs in the same context
            Debug.Assert(Object.ReferenceEquals(o, null) || !(o is MethodDesc) || Object.ReferenceEquals(((MethodDesc)o).Context, this.Context));
            return Object.ReferenceEquals(this, o);
        }

        /// <summary>
        /// Gets the type that owns this method. This will be a <see cref="DefType"/> or
        /// an <see cref="ArrayType"/>.
        /// </summary>
        public abstract TypeDesc OwningType
        {
            get;
        }

        /// <summary>
        /// Gets the signature of the method.
        /// </summary>
        public abstract MethodSignature Signature
        {
            get;
        }

        /// <summary>
        /// Gets the generic instantiation information of this method.
        /// For generic definitions, retrieves the generic parameters of the method.
        /// For generic instantiation, retrieves the generic arguments of the method.
        /// </summary>
        public virtual Instantiation Instantiation
        {
            get
            {
                return Instantiation.Empty;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this method has a generic instantiation.
        /// This will be true for generic method instantiations and generic definitions.
        /// </summary>
        public bool HasInstantiation
        {
            get
            {
                return this.Instantiation.Length != 0;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this method is an instance constructor.
        /// </summary>
        public bool IsConstructor
        {
            get
            {
                // TODO: Precise check
                // TODO: Cache?
                return this.Name == ".ctor";
            }
        }

        /// <summary>
        /// Gets a value indicating whether this is a public parameterless instance constructor
        /// on a non-abstract type.
        /// </summary>
        public virtual bool IsDefaultConstructor
        {
            get
            {
                return OwningType.GetDefaultConstructor() == this;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this method is a static constructor.
        /// </summary>
        public bool IsStaticConstructor
        {
            get
            {
                return this == this.OwningType.GetStaticConstructor();
            }
        }

        /// <summary>
        /// Gets the name of the method as specified in the metadata.
        /// </summary>
        public virtual string Name
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the method is virtual.
        /// </summary>
        public virtual bool IsVirtual
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this virtual method should not override any
        /// virtual methods defined in any of the base classes.
        /// </summary>
        public virtual bool IsNewSlot
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this virtual method needs to be overriden
        /// by all non-abstract classes deriving from the method's owning type.
        /// </summary>
        public virtual bool IsAbstract
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating that this method cannot be overriden.
        /// </summary>
        public virtual bool IsFinal
        {
            get
            {
                return false;
            }
        }

        public abstract bool HasCustomAttribute(string attributeNamespace, string attributeName);

        /// <summary>
        /// Retrieves the uninstantiated form of the method on the method's <see cref="OwningType"/>.
        /// For generic methods, this strips method instantiation. For non-generic methods, returns 'this'.
        /// To also strip instantiation of the owning type, use <see cref="GetTypicalMethodDefinition"/>.
        /// </summary>
        public virtual MethodDesc GetMethodDefinition()
        {
            return this;
        }

        /// <summary>
        /// Gets a value indicating whether this is a method definition. This property
        /// is true for non-generic methods and for uninstantiated generic methods.
        /// </summary>
        public bool IsMethodDefinition
        {
            get
            {
                return GetMethodDefinition() == this;
            }
        }

        /// <summary>
        /// Retrieves the generic definition of the method on the generic definition of the owning type.
        /// To only uninstantiate the method without uninstantiating the owning type, use <see cref="GetMethodDefinition"/>.
        /// </summary>
        public virtual MethodDesc GetTypicalMethodDefinition()
        {
            return this;
        }

        /// <summary>
        /// Gets a value indicating whether this is a typical definition. This property is true
        /// if neither the owning type, nor the method are instantiated.
        /// </summary>
        public bool IsTypicalMethodDefinition
        {
            get
            {
                return GetTypicalMethodDefinition() == this;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this is an uninstantiated generic method.
        /// </summary>
        public bool IsGenericMethodDefinition
        {
            get
            {
                return HasInstantiation && IsMethodDefinition;
            }
        }

        public bool IsFinalizer
        {
            get
            {
                TypeDesc owningType = OwningType;
                return (owningType.IsObject && Name == "Finalize") || (owningType.HasFinalizer && owningType.GetFinalizer() == this);
            }
        }

        public virtual MethodDesc InstantiateSignature(Instantiation typeInstantiation, Instantiation methodInstantiation)
        {
            Instantiation instantiation = Instantiation;
            TypeDesc[] clone = null;

            for (int i = 0; i < instantiation.Length; i++)
            {
                TypeDesc uninst = instantiation[i];
                TypeDesc inst = uninst.InstantiateSignature(typeInstantiation, methodInstantiation);
                if (inst != uninst)
                {
                    if (clone == null)
                    {
                        clone = new TypeDesc[instantiation.Length];
                        for (int j = 0; j < clone.Length; j++)
                        {
                            clone[j] = instantiation[j];
                        }
                    }
                    clone[i] = inst;
                }
            }

            MethodDesc method = this;

            TypeDesc owningType = method.OwningType;
            TypeDesc instantiatedOwningType = owningType.InstantiateSignature(typeInstantiation, methodInstantiation);
            if (owningType != instantiatedOwningType)
            {
                method = Context.GetMethodForInstantiatedType(method.GetTypicalMethodDefinition(), (InstantiatedType)instantiatedOwningType);
                if (clone == null && instantiation.Length != 0)
                    return Context.GetInstantiatedMethod(method, instantiation);
            }

            return (clone == null) ? method : Context.GetInstantiatedMethod(method.GetMethodDefinition(), new Instantiation(clone));
        }
    }
}
