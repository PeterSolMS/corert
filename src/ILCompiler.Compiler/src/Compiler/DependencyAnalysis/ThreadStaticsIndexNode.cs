// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using Internal.Text;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    // These classes defined below are Windows-specific. The VC CRT library has equivalent definition
    // for ThreadStaticsIndexNode and ThreadStaticsDirectoryNode, but it does not support cross-module
    // TLS references where the name of _tls_index_ will need to be module-sensitive. Therefore, we
    // define them here.

    // The TLS slot index allocated for this module by the OS loader. We keep a pointer to this
    // value in the module header.
    public class ThreadStaticsIndexNode : ObjectNode, ISymbolNode
    {
        string _prefix;

        public ThreadStaticsIndexNode(string prefix)  
        {
            _prefix = prefix;
        }

        public string MangledName
        {
            get
            {
                return GetMangledName(_prefix);
            }
        }

        public static string GetMangledName(string prefix)
        {
            return  "_tls_index_" + prefix;
        }

        public int Offset => 0;
        
        protected override string GetName() => this.GetMangledName();

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(GetMangledName(_prefix));
        }        

        public override ObjectNodeSection Section
        {
            get
            {
                return ObjectNodeSection.DataSection;
            }
        }

        public override bool IsShareable => false;            

        public override bool StaticDependenciesAreComputed => true;

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            // TODO: define _tls_index as "comdat select any" when multiple object files present.

            ObjectDataBuilder objData = new ObjectDataBuilder(factory);
            objData.RequireInitialPointerAlignment();
            objData.AddSymbol(this);

            // Emit an aliased symbol named _tls_index for native P/Invoke code that uses TLS. This is required
            // because we do not link against libcmt.lib.
            ObjectAndOffsetSymbolNode aliasedSymbol = new ObjectAndOffsetSymbolNode(this, objData.CountBytes, "_tls_index", false);
            objData.AddSymbol(aliasedSymbol);

            // This is the TLS index field which is a 4-byte integer. 
            objData.EmitInt(0); 

            return objData.ToObjectData();
        }
    }

    // The data structure used by the OS loader to load TLS chunks. 
    public class ThreadStaticsDirectoryNode : ObjectNode, ISymbolNode
    {
        string _prefix;
        public ThreadStaticsDirectoryNode(string prefix)
        {
            _prefix = prefix;
        }

        public string MangledName
        {
            get
            {
                return GetMangledName(_prefix);
            }
        }

        public static string GetMangledName(string prefix)
        {
            return prefix + "_tls_used";
        }

        public int Offset => 0;

        protected override string GetName() => GetMangledName("");

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(GetMangledName(_prefix));
        }

        public override ObjectNodeSection Section
        {
            get
            {
                return ObjectNodeSection.ReadOnlyDataSection;
            }
        }

        public override bool IsShareable => false;

        public override bool StaticDependenciesAreComputed => true;

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            // TODO: define _tls_used as comdat select any when multiple object files present.
            UtcNodeFactory hostedFactory = factory as UtcNodeFactory;
            ObjectDataBuilder objData = new ObjectDataBuilder(factory);
            objData.RequireInitialPointerAlignment();
            objData.AddSymbol(this);

            // Allocate and initialize the IMAGE_TLS_DIRECTORY PE data structure used by the OS loader to determine
            // TLS allocations. The structure is defined by the OS as following:
            /*
                struct _IMAGE_TLS_DIRECTORY32
                {
                    DWORD StartAddressOfRawData;
                    DWORD EndAddressOfRawData;
                    DWORD AddressOfIndex;
                    DWORD AddressOfCallBacks;
                    DWORD SizeOfZeroFill;
                    DWORD Characteristics;
                }

                struct _IMAGE_TLS_DIRECTORY64
                {
                    ULONGLONG StartAddressOfRawData;
                    ULONGLONG EndAddressOfRawData;
                    ULONGLONG AddressOfIndex;
                    ULONGLONG AddressOfCallBacks;
                    DWORD SizeOfZeroFill;
                    DWORD Characteristics;
                }
            */
            // In order to utilize linker support, the struct variable needs to be named _tls_used
            objData.EmitPointerReloc(hostedFactory.TlsStart);     // start of tls data
            objData.EmitPointerReloc(hostedFactory.TlsEnd);     // end of tls data
            objData.EmitPointerReloc(hostedFactory.ThreadStaticsIndex);     // address of tls_index
            objData.EmitZeroPointer();          // pointer to call back array
            objData.EmitInt(0);                 // size of tls zero fill
            objData.EmitInt(0);                 // characteristics

            return objData.ToObjectData();
        }
    }

    // The offset into the TLS block at which managed data begins. 
    public class ThreadStaticsStartNode : ObjectNode, ISymbolNode
    {
        public ThreadStaticsStartNode()
        {
        }

        public string MangledName
        {
            get
            {
                return GetMangledName();
            }
        }

        public static string GetMangledName()
        {
            return "_tls_offset";
        }

        public int Offset => 0;

        protected override string GetName() => GetMangledName();

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(GetMangledName());
        }

        public override ObjectNodeSection Section
        {
            get
            {
                return ObjectNodeSection.ReadOnlyDataSection;
            }
        }

        public override bool IsShareable => false;

        public override bool ShouldSkipEmittingObjectNode(NodeFactory factory)
        {
            return factory.ThreadStaticsRegion.ShouldSkipEmittingObjectNode(factory);
        }

        public override bool StaticDependenciesAreComputed => true;
            
        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            ObjectDataBuilder objData = new ObjectDataBuilder(factory);
            objData.RequireInitialPointerAlignment();
            objData.AddSymbol(this);
            objData.EmitReloc(factory.ThreadStaticsRegion.StartSymbol, RelocType.IMAGE_REL_SECREL);
            return objData.ToObjectData();
        }
    }
}