using ChocolArm64.Decoders;
using ChocolArm64.State;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.Intrinsics;

namespace ChocolArm64.Translation
{
    class ILEmitter
    {
        public LocalAlloc LocalAlloc { get; private set; }

        public ILGenerator Generator { get; private set; }

        private Dictionary<Register, int> _locals;

        private ILBlock[] _ilBlocks;

        private ILBlock _root;

        private string _subName;

        private int _localsCount;

        public ILEmitter(Block[] graph, Block root, string subName)
        {
            _subName = subName;

            _ilBlocks = new ILBlock[graph.Length];

            ILBlock GetBlock(int index)
            {
                if (index < 0 || index >= _ilBlocks.Length)
                {
                    return null;
                }

                if (_ilBlocks[index] == null)
                {
                    _ilBlocks[index] = new ILBlock();
                }

                return _ilBlocks[index];
            }

            for (int index = 0; index < _ilBlocks.Length; index++)
            {
                ILBlock block = GetBlock(index);

                block.Next   = GetBlock(Array.IndexOf(graph, graph[index].Next));
                block.Branch = GetBlock(Array.IndexOf(graph, graph[index].Branch));
            }

            _root = _ilBlocks[Array.IndexOf(graph, root)];
        }

        public ILBlock GetIlBlock(int index) => _ilBlocks[index];

        public TranslatedSub GetSubroutine(TranslationCodeQuality cq)
        {
            LocalAlloc = new LocalAlloc(_ilBlocks, _root);

            List<Register> subArgs = new List<Register>();

            void SetParams(long inputs, RegisterType baseType)
            {
                for (int bit = 0; bit < 64; bit++)
                {
                    long mask = 1L << bit;

                    if ((inputs & mask) != 0)
                    {
                        subArgs.Add(GetRegFromBit(bit, baseType));
                    }
                }
            }

            SetParams(LocalAlloc.GetIntInputs(_root), RegisterType.Int);
            SetParams(LocalAlloc.GetVecInputs(_root), RegisterType.Vector);

            DynamicMethod mthd = new DynamicMethod(_subName, typeof(long), GetParamTypes(subArgs));

            Generator = mthd.GetILGenerator();

            TranslatedSub subroutine = new TranslatedSub(mthd, subArgs, cq);

            int argsStart = TranslatedSub.FixedArgTypes.Length;

            _locals = new Dictionary<Register, int>();

            _localsCount = 0;

            for (int index = 0; index < subroutine.SubArgs.Count; index++)
            {
                Register reg = subroutine.SubArgs[index];

                Generator.EmitLdarg(index + argsStart);
                Generator.EmitStloc(GetLocalIndex(reg));
            }

            foreach (ILBlock ilBlock in _ilBlocks)
            {
                ilBlock.Emit(this);
            }

            return subroutine;
        }

        private Type[] GetParamTypes(IList<Register> Params)
        {
            Type[] fixedArgs = TranslatedSub.FixedArgTypes;

            Type[] output = new Type[Params.Count + fixedArgs.Length];

            fixedArgs.CopyTo(output, 0);

            int typeIdx = fixedArgs.Length;

            for (int index = 0; index < Params.Count; index++)
            {
                output[typeIdx++] = GetFieldType(Params[index].Type);
            }

            return output;
        }

        public int GetLocalIndex(Register reg)
        {
            if (!_locals.TryGetValue(reg, out int index))
            {
                Generator.DeclareLocal(GetLocalType(reg));

                index = _localsCount++;

                _locals.Add(reg, index);
            }

            return index;
        }

        public Type GetLocalType(Register reg) => GetFieldType(reg.Type);

        public Type GetFieldType(RegisterType regType)
        {
            switch (regType)
            {
                case RegisterType.Flag:   return typeof(bool);
                case RegisterType.Int:    return typeof(ulong);
                case RegisterType.Vector: return typeof(Vector128<float>);
            }

            throw new ArgumentException(nameof(regType));
        }

        public static Register GetRegFromBit(int bit, RegisterType baseType)
        {
            if (bit < 32)
            {
                return new Register(bit, baseType);
            }
            else if (baseType == RegisterType.Int)
            {
                return new Register(bit & 0x1f, RegisterType.Flag);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(bit));
            }
        }

        public static bool IsRegIndex(int index)
        {
            return index >= 0 && index < 32;
        }
    }
}