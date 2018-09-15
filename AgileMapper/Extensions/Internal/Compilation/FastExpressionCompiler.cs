﻿/*
The MIT License (MIT)
Copyright (c) 2016 Maksim Volkau
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included AddOrUpdateServiceFactory
all copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

// ReSharper disable CoVariantArrayConversion

#if LIGHT_EXPRESSION
namespace FastExpressionCompiler.LightExpression
#else
namespace AgileObjects.AgileMapper.Extensions.Internal.Compilation
#endif
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using Internal;
    using NetStandardPolyfills;
#if NET35
    using Microsoft.Scripting.Ast;
#else
    using System.Linq.Expressions;
#endif

    /// <summary>Compiles expression to delegate ~20 times faster than Expression.Compile.
    /// Partial to extend with your things when used as source file.</summary>
    // ReSharper disable once PartialTypeWithSinglePart
    internal static partial class FastExpressionCompiler
    {
#if !FEATURE_FAST_COMPILE
        public static Func<TR> CompileFast<TR>(this Expression<Func<TR>> lambdaExpr) => lambdaExpr.Compile();

        public static Func<T, TR> CompileFast<T, TR>(this Expression<Func<T, TR>> lambdaExpr) => lambdaExpr.Compile();

        public static Func<T1, T2, TR> CompileFast<T1, T2, TR>(this Expression<Func<T1, T2, TR>> lambdaExpr) => lambdaExpr.Compile();

        public static Func<T1, T2, T3, TR> CompileFast<T1, T2, T3, TR>(this Expression<Func<T1, T2, T3, TR>> lambdaExpr)
            => lambdaExpr.Compile();

        public static TDelegate CompileFast<TDelegate>(this Expression<TDelegate> lambdaExpr)
            where TDelegate : class
        {
            return lambdaExpr.Compile();
        }
#else
        #region Expression.CompileFast overloads for Delegate, Funcs, and Actions

        public static TDelegate CompileFast<TDelegate>(this LambdaExpression lambdaExpr)
            where TDelegate : class
        {
            return TryCompile<TDelegate>(
                       lambdaExpr.Body,
                       lambdaExpr.Parameters,
                       Tools.GetParamTypes(lambdaExpr.Parameters),
                       lambdaExpr.ReturnType)
                   ?? (TDelegate)(object)lambdaExpr
#if LIGHT_EXPRESSION
                       .ToLambdaExpression()
#endif
                       .Compile();
        }

        private static TDelegate CompileSys<TDelegate>(this Expression<TDelegate> lambdaExpr)
            where TDelegate : class
        {
            return lambdaExpr
#if LIGHT_EXPRESSION
            .ToLambdaExpression()
#endif
                .Compile();
        }

        public static TDelegate CompileFast<TDelegate>(this Expression<TDelegate> lambdaExpr)
            where TDelegate : class
        {
            return ((LambdaExpression)lambdaExpr).CompileFast<TDelegate>();
        }

        public static Func<TR> CompileFast<TR>(this Expression<Func<TR>> lambdaExpr)
        {
            return TryCompile<Func<TR>>(
                   lambdaExpr.Body,
                   lambdaExpr.Parameters,
                   Constants.NoTypeArguments,
                   typeof(TR))
                ?? lambdaExpr.CompileSys();
        }

        public static Func<T, TR> CompileFast<T, TR>(this Expression<Func<T, TR>> lambdaExpr)
        {
            return TryCompile<Func<T, TR>>(
                    lambdaExpr.Body,
                   lambdaExpr.Parameters,
                   new[] { typeof(T) },
                   typeof(TR))
                   ?? lambdaExpr.CompileSys();
        }

        public static Func<T1, T2, TR> CompileFast<T1, T2, TR>(this Expression<Func<T1, T2, TR>> lambdaExpr)
        {
            return TryCompile<Func<T1, T2, TR>>(
                   lambdaExpr.Body,
                   lambdaExpr.Parameters,
                   new[] { typeof(T1), typeof(T2) },
                   typeof(TR))
                   ?? lambdaExpr.CompileSys();
        }

        public static Func<T1, T2, T3, TR> CompileFast<T1, T2, T3, TR>(this Expression<Func<T1, T2, T3, TR>> lambdaExpr)
        {
            return TryCompile<Func<T1, T2, T3, TR>>(
               lambdaExpr.Body,
               lambdaExpr.Parameters,
               new[] { typeof(T1), typeof(T2), typeof(T3) },
               typeof(TR))
                ?? lambdaExpr.CompileSys();
        }

        #endregion

        /// <summary>Compiles expression to delegate by emitting the IL. 
        /// If sub-expressions are not supported by emitter, then the method returns null.
        /// The usage should be calling the method, if result is null then calling the Expression.Compile.</summary>
        public static TDelegate TryCompile<TDelegate>(
            Expression bodyExpr,
            IList<ParameterExpression> paramExprs,
            Type[] paramTypes,
            Type returnType)
            where TDelegate : class
        {
            var ignored = new ClosureInfo(false);

            return (TDelegate)TryCompile(
                ref ignored,
                typeof(TDelegate),
                paramTypes,
                returnType,
                bodyExpr,
                paramExprs);
        }

        private static object TryCompile(
            ref ClosureInfo closureInfo,
            Type delegateType,
            Type[] paramTypes,
            Type returnType,
            Expression expr,
            IList<ParameterExpression> paramExprs,
            bool isNestedLambda = false)
        {
            object closureObject;
            if (closureInfo.IsClosureConstructed)
            {
                closureObject = closureInfo.Closure;
            }
            else if (TryCollectBoundConstants(ref closureInfo, expr, paramExprs))
            {
                var nestedLambdaExprs = closureInfo.NestedLambdaExprs;
                var nestedLambdaCount = nestedLambdaExprs.Length;
                if (nestedLambdaCount != 0)
                {
                    closureInfo.NestedLambdas = new NestedLambdaInfo[nestedLambdaCount];
                    for (var i = 0; i < nestedLambdaCount; ++i)
                    {
                        if (!TryCompileNestedLambda(ref closureInfo, i, nestedLambdaExprs[i]))
                        {
                            return null;
                        }
                    }
                }

                closureObject = closureInfo.ConstructClosureTypeAndObject(constructTypeOnly: isNestedLambda);
            }
            else
            {
                return null;
            }

            if (closureInfo.LabelCount > 0)
            {
                closureInfo.Labels = new KeyValuePair<object, Label>[closureInfo.LabelCount];
            }

            closureInfo.LabelCount = 0;

            var closureType = closureInfo.ClosureType;
            var methodParamTypes = closureType == null ? paramTypes : GetClosureAndParamTypes(paramTypes, closureType);

            var method = new DynamicMethod(string.Empty, returnType, methodParamTypes,
                typeof(FastExpressionCompiler), skipVisibility: true);

            var il = method.GetILGenerator();

            var parentFlags = returnType == typeof(void) ? ParentFlags.IgnoreResult : ParentFlags.Empty;

            if (!EmittingVisitor.TryEmit(expr, paramExprs, il, ref closureInfo, parentFlags))
            {
                return null;
            }

            il.Emit(OpCodes.Ret);

            // include closure as the first parameter, BUT don't bound to it. It will be bound later in EmitNestedLambda.
            if (isNestedLambda)
            {
                delegateType = Tools.GetFuncOrActionType(methodParamTypes, returnType);
            }
            // create a specific delegate if user requested delegate is untyped, otherwise CreateMethod will fail
            else if (delegateType == typeof(Delegate))
            {
                delegateType = Tools.GetFuncOrActionType(paramTypes, returnType);
            }

            return method.CreateDelegate(delegateType, closureObject);
        }

        private static void CopyNestedClosureInfo(
            IList<ParameterExpression> lambdaParamExprs,
            ref ClosureInfo info,
            ref ClosureInfo nestedInfo)
        {
            // if nested non passed parameter is no matched with any outer passed parameter, 
            // then ensure it goes to outer non passed parameter.
            // But check that having a non-passed parameter in root expression is invalid.
            var nestedNonPassedParams = nestedInfo.NonPassedParameters;
            if (nestedNonPassedParams.Length != 0)
            {
                for (var i = 0; i < nestedNonPassedParams.Length; i++)
                {
                    var nestedNonPassedParam = nestedNonPassedParams[i];
                    if (lambdaParamExprs.GetFirstIndex(nestedNonPassedParam) == -1)
                    {
                        info.AddNonPassedParam(nestedNonPassedParam);
                    }
                }
            }

            // Promote found constants and nested lambdas into outer closure
            var nestedConstants = nestedInfo.Constants;
            if (nestedConstants.Length != 0)
            {
                for (var i = 0; i < nestedConstants.Length; i++)
                {
                    info.AddConstant(nestedConstants[i]);
                }
            }

            // Add nested constants to outer lambda closure.
            // At this moment we  know that NestedLambdaExprs are non-empty, cause we doing this from the nested lambda already.
            var nestedNestedLambdaExprs = nestedInfo.NestedLambdaExprs;
            if (nestedNestedLambdaExprs.Length != 0)
            {
                var fixedNestedLambdaCount = info.NestedLambdaExprs.Length;
                for (var i = 0; i < nestedNestedLambdaExprs.Length; i++)
                {
                    var nestedNestedLambdaExpr = nestedNestedLambdaExprs[i];

                    var j = info.NestedLambdaExprs.Length - 1;
                    for (; j >= fixedNestedLambdaCount; --j)
                    {
                        if (ReferenceEquals(info.NestedLambdaExprs[j], nestedNestedLambdaExpr))
                        {
                            break;
                        }
                    }

                    if (j < fixedNestedLambdaCount)
                    {
                        info.NestedLambdaExprs = info.NestedLambdaExprs.Append(nestedNestedLambdaExpr);
                        info.NestedLambdas = info.NestedLambdas.Append(nestedInfo.NestedLambdas[i]);
                    }
                }
            }
        }

        private static Type[] GetClosureAndParamTypes(Type[] paramTypes, Type closureType)
        {
            var paramCount = paramTypes.Length;
            if (paramCount == 0)
            {
                return new[] { closureType };
            }

            if (paramCount == 1)
            {
                return new[] { closureType, paramTypes[0] };
            }

            var closureAndParamTypes = new Type[paramCount + 1];
            closureAndParamTypes[0] = closureType;
            Array.Copy(paramTypes, 0, closureAndParamTypes, 1, paramCount);
            return closureAndParamTypes;
        }

        private sealed class BlockInfo
        {
            public static readonly BlockInfo Empty = new BlockInfo();
            private BlockInfo() { }

            public bool IsEmpty => Parent == null;
            public readonly BlockInfo Parent;
            public readonly IList<ParameterExpression> VarExprs;
            public readonly LocalBuilder[] LocalVars;

            internal BlockInfo(BlockInfo parent, IList<ParameterExpression> varExprs, LocalBuilder[] localVars)
            {
                Parent = parent;
                VarExprs = varExprs;
                LocalVars = localVars;
            }
        }

        // Track the info required to build a closure object + some context information not directly related to closure.
        private struct ClosureInfo
        {
            public bool IsClosureConstructed;

            // Constructed closure object.
            public readonly object Closure;

            // Type of constructed closure, may be available even without closure object (in case of nested lambda)
            public Type ClosureType;

            public bool HasClosure => ClosureType != null;

            public bool LastEmitIsAddress;

            // Constant expressions to find an index (by reference) of constant expression from compiled expression.
            public ConstantExpression[] Constants;

            // Parameters not passed through lambda parameter list But used inside lambda body.
            // The top expression should not! contain non passed parameters. 
            public ParameterExpression[] NonPassedParameters;

            // All nested lambdas recursively nested in expression
            public NestedLambdaInfo[] NestedLambdas;
            public LambdaExpression[] NestedLambdaExprs;

            public Dictionary<Expression, Expression> ReducedExpressions;

            public int ClosedItemCount
                => Constants.Length + NonPassedParameters.Length + NestedLambdas.Length;

            // FieldInfos are needed to load field of closure object on stack in emitter.
            // It is also an indicator that we use typed Closure object and not an array.
            public FieldInfo[] ClosureFields;

            // Helper to decide whether we are inside the block or not
            public BlockInfo CurrentBlock;

            public int LabelCount;

            // Dictionary for the used Labels in IL
            public KeyValuePair<object, Label>[] Labels;

            // Populates info directly with provided closure object and constants.
            public ClosureInfo(bool isConstructed, object closure = null,
                ConstantExpression[] closureConstantExpressions = null)
            {
                IsClosureConstructed = isConstructed;

                NonPassedParameters = Enumerable<ParameterExpression>.EmptyArray;
                NestedLambdas = Enumerable<NestedLambdaInfo>.EmptyArray;
                NestedLambdaExprs = Enumerable<LambdaExpression>.EmptyArray;
                CurrentBlock = BlockInfo.Empty;
                Labels = null;
                LabelCount = 0;
                LastEmitIsAddress = false;
                ReducedExpressions = null;

                if (closure == null)
                {
                    Closure = null;
                    Constants = Enumerable<ConstantExpression>.EmptyArray;
                    ClosureType = null;
                    ClosureFields = null;
                }
                else
                {
                    Closure = closure;
                    Constants = closureConstantExpressions ?? Enumerable<ConstantExpression>.EmptyArray;
                    ClosureType = closure.GetType();
                    // todo: verify that Fields types are correspond to `closureConstantExpressions`
                    ClosureFields = ClosureType.GetPublicInstanceFields().ToArray();
                }
            }

            public void AddConstant(ConstantExpression expr)
            {
                if (Constants.Length == 0 ||
                    Constants.GetFirstIndex(expr) == -1)
                {
                    Constants = Constants.Append(expr);
                }
            }

            public void AddNonPassedParam(ParameterExpression expr)
            {
                if (NonPassedParameters.Length == 0 ||
                    NonPassedParameters.GetFirstIndex(expr) == -1)
                {
                    NonPassedParameters = NonPassedParameters.Append(expr);
                }
            }

            public void AddNestedLambda(LambdaExpression lambdaExpr)
            {
                if (NestedLambdaExprs.Length == 0 ||
                    NestedLambdaExprs.GetFirstIndex(lambdaExpr) == -1)
                {
                    NestedLambdaExprs = NestedLambdaExprs.Append(lambdaExpr);
                }
            }

            public object ConstructClosureTypeAndObject(bool constructTypeOnly)
            {
                IsClosureConstructed = true;

                var constants = Constants;
                var nonPassedParams = NonPassedParameters;
                var nestedLambdas = NestedLambdas;
                if (constants.Length == 0 && nonPassedParams.Length == 0 && nestedLambdas.Length == 0)
                {
                    return null;
                }

                var constPlusParamCount = constants.Length + nonPassedParams.Length;
                var totalItemCount = constPlusParamCount + nestedLambdas.Length;

                // Construct the array based closure when number of values is bigger than
                // number of fields in biggest supported Closure class.
                var createMethods = FastExpressionCompiler.Closure.CreateMethods;
                if (totalItemCount > createMethods.Length)
                {
                    ClosureType = typeof(ArrayClosure);
                    if (constructTypeOnly)
                    {
                        return null;
                    }

                    var items = new object[totalItemCount];
                    if (constants.Length != 0)
                    {
                        for (var i = 0; i < constants.Length; i++)
                        {
                            items[i] = constants[i].Value;
                        }
                    }

                    // skip non passed parameters as it is only for nested lambdas

                    if (nestedLambdas.Length != 0)
                    {
                        for (var i = 0; i < nestedLambdas.Length; i++)
                        {
                            items[constPlusParamCount + i] = nestedLambdas[i].Lambda;
                        }
                    }

                    return new ArrayClosure(items);
                }

                // Construct the Closure Type and optionally Closure object with closed values stored as fields:
                object[] fieldValues = null;
                var fieldTypes = new Type[totalItemCount];
                if (constructTypeOnly)
                {
                    if (constants.Length != 0)
                    {
                        for (var i = 0; i < constants.Length; i++)
                        {
                            fieldTypes[i] = constants[i].Type;
                        }
                    }

                    if (nonPassedParams.Length != 0)
                    {
                        for (var i = 0; i < nonPassedParams.Length; i++)
                        {
                            fieldTypes[constants.Length + i] = nonPassedParams[i].Type;
                        }
                    }

                    if (nestedLambdas.Length != 0)
                    {
                        for (var i = 0; i < nestedLambdas.Length; i++)
                        {
                            fieldTypes[constPlusParamCount + i] =
                                nestedLambdas[i].Lambda.GetType(); // compiled lambda type
                        }
                    }
                }
                else
                {
                    fieldValues = new object[totalItemCount];

                    if (constants.Length != 0)
                    {
                        for (var i = 0; i < constants.Length; i++)
                        {
                            var constantExpr = constants[i];
                            if (constantExpr != null)
                            {
                                fieldTypes[i] = constantExpr.Type;
                                fieldValues[i] = constantExpr.Value;
                            }
                        }
                    }

                    if (nonPassedParams.Length != 0)
                    {
                        for (var i = 0; i < nonPassedParams.Length; i++)
                        {
                            fieldTypes[constants.Length + i] = nonPassedParams[i].Type;
                        }
                    }

                    if (nestedLambdas.Length != 0)
                    {
                        for (var i = 0; i < nestedLambdas.Length; i++)
                        {
                            var lambda = nestedLambdas[i].Lambda;
                            fieldValues[constPlusParamCount + i] = lambda;
                            fieldTypes[constPlusParamCount + i] = lambda.GetType();
                        }
                    }
                }

                var createClosure = createMethods[totalItemCount - 1].MakeGenericMethod(fieldTypes);
                ClosureType = createClosure.ReturnType;
                ClosureFields = ClosureType.GetPublicInstanceFields().ToArray();

                return constructTypeOnly ? null : createClosure.Invoke(null, fieldValues);
            }

            public void PushBlock(
                IList<ParameterExpression> blockVarExprs,
                LocalBuilder[] localVars)
            {
                CurrentBlock = new BlockInfo(CurrentBlock, blockVarExprs, localVars);
            }

            public void PushBlockAndConstructLocalVars(
                IList<ParameterExpression> blockVarExprs,
                ILGenerator il)
            {
                LocalBuilder[] localVars;
                if (blockVarExprs.Count != 0)
                {
                    localVars = new LocalBuilder[blockVarExprs.Count];
                    for (var i = 0; i < localVars.Length; i++)
                    {
                        localVars[i] = il.DeclareLocal(blockVarExprs[i].Type);
                    }
                }
                else
                {
                    localVars = Enumerable<LocalBuilder>.EmptyArray;
                }

                CurrentBlock = new BlockInfo(CurrentBlock, blockVarExprs, localVars);
            }

            public void PopBlock() => CurrentBlock = CurrentBlock.Parent;

            public bool IsLocalVar(Expression varParamExpr)
            {
                var i = -1;
                for (var block = CurrentBlock; i == -1 && !block.IsEmpty; block = block.Parent)
                {
                    i = block.VarExprs.GetFirstIndex(varParamExpr);
                }

                return i != -1;
            }

            public LocalBuilder GetDefinedLocalVarOrDefault(ParameterExpression varParamExpr)
            {
                for (var block = CurrentBlock; !block.IsEmpty; block = block.Parent)
                {
                    if (block.LocalVars.Length == 0)
                    {
                        continue;
                    }

                    var varIndex = block.VarExprs.GetFirstIndex(varParamExpr);
                    if (varIndex != -1)
                    {
                        return block.LocalVars[varIndex];
                    }
                }

                return null;
            }
        }

        #region Closures

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

        public static class Closure
        {
            internal static readonly MethodInfo[] CreateMethods = typeof(Closure).GetPublicStaticMethods("Create").ToArray();

            public static Closure<T1> Create<T1>(T1 v1) => new Closure<T1>(v1);

            public static Closure<T1, T2> Create<T1, T2>(T1 v1, T2 v2) => new Closure<T1, T2>(v1, v2);

            public static Closure<T1, T2, T3> Create<T1, T2, T3>(T1 v1, T2 v2, T3 v3) =>
                new Closure<T1, T2, T3>(v1, v2, v3);

            public static Closure<T1, T2, T3, T4> Create<T1, T2, T3, T4>(T1 v1, T2 v2, T3 v3, T4 v4) =>
                new Closure<T1, T2, T3, T4>(v1, v2, v3, v4);

            public static Closure<T1, T2, T3, T4, T5> Create<T1, T2, T3, T4, T5>(T1 v1, T2 v2, T3 v3, T4 v4,
                T5 v5) => new Closure<T1, T2, T3, T4, T5>(v1, v2, v3, v4, v5);

            public static Closure<T1, T2, T3, T4, T5, T6> Create<T1, T2, T3, T4, T5, T6>(T1 v1, T2 v2, T3 v3,
                T4 v4, T5 v5, T6 v6) => new Closure<T1, T2, T3, T4, T5, T6>(v1, v2, v3, v4, v5, v6);

            public static Closure<T1, T2, T3, T4, T5, T6, T7> Create<T1, T2, T3, T4, T5, T6, T7>(T1 v1, T2 v2,
                T3 v3, T4 v4, T5 v5, T6 v6, T7 v7) =>
                new Closure<T1, T2, T3, T4, T5, T6, T7>(v1, v2, v3, v4, v5, v6, v7);

            public static Closure<T1, T2, T3, T4, T5, T6, T7, T8> Create<T1, T2, T3, T4, T5, T6, T7, T8>(
                T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7, T8 v8) =>
                new Closure<T1, T2, T3, T4, T5, T6, T7, T8>(v1, v2, v3, v4, v5, v6, v7, v8);

            public static Closure<T1, T2, T3, T4, T5, T6, T7, T8, T9> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
                T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7, T8 v8, T9 v9) =>
                new Closure<T1, T2, T3, T4, T5, T6, T7, T8, T9>(v1, v2, v3, v4, v5, v6, v7, v8, v9);

            public static Closure<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9,
                T10>(
                T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7, T8 v8, T9 v9, T10 v10) =>
                new Closure<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(v1, v2, v3, v4, v5, v6, v7, v8, v9, v10);
        }

        public sealed class Closure<T1>
        {
            public T1 V1;

            public Closure(T1 v1)
            {
                V1 = v1;
            }
        }

        public sealed class Closure<T1, T2>
        {
            public T1 V1;
            public T2 V2;

            public Closure(T1 v1, T2 v2)
            {
                V1 = v1;
                V2 = v2;
            }
        }

        public sealed class Closure<T1, T2, T3>
        {
            public T1 V1;
            public T2 V2;
            public T3 V3;

            public Closure(T1 v1, T2 v2, T3 v3)
            {
                V1 = v1;
                V2 = v2;
                V3 = v3;
            }
        }

        public sealed class Closure<T1, T2, T3, T4>
        {
            public T1 V1;
            public T2 V2;
            public T3 V3;
            public T4 V4;

            public Closure(T1 v1, T2 v2, T3 v3, T4 v4)
            {
                V1 = v1;
                V2 = v2;
                V3 = v3;
                V4 = v4;
            }
        }

        public sealed class Closure<T1, T2, T3, T4, T5>
        {
            public T1 V1;
            public T2 V2;
            public T3 V3;
            public T4 V4;
            public T5 V5;

            public Closure(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5)
            {
                V1 = v1;
                V2 = v2;
                V3 = v3;
                V4 = v4;
                V5 = v5;
            }
        }

        public sealed class Closure<T1, T2, T3, T4, T5, T6>
        {
            public T1 V1;
            public T2 V2;
            public T3 V3;
            public T4 V4;
            public T5 V5;
            public T6 V6;

            public Closure(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6)
            {
                V1 = v1;
                V2 = v2;
                V3 = v3;
                V4 = v4;
                V5 = v5;
                V6 = v6;
            }
        }

        public sealed class Closure<T1, T2, T3, T4, T5, T6, T7>
        {
            public T1 V1;
            public T2 V2;
            public T3 V3;
            public T4 V4;
            public T5 V5;
            public T6 V6;
            public T7 V7;

            public Closure(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7)
            {
                V1 = v1;
                V2 = v2;
                V3 = v3;
                V4 = v4;
                V5 = v5;
                V6 = v6;
                V7 = v7;
            }
        }

        public sealed class Closure<T1, T2, T3, T4, T5, T6, T7, T8>
        {
            public T1 V1;
            public T2 V2;
            public T3 V3;
            public T4 V4;
            public T5 V5;
            public T6 V6;
            public T7 V7;
            public T8 V8;

            public Closure(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7, T8 v8)
            {
                V1 = v1;
                V2 = v2;
                V3 = v3;
                V4 = v4;
                V5 = v5;
                V6 = v6;
                V7 = v7;
                V8 = v8;
            }
        }

        public sealed class Closure<T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {
            public T1 V1;
            public T2 V2;
            public T3 V3;
            public T4 V4;
            public T5 V5;
            public T6 V6;
            public T7 V7;
            public T8 V8;
            public T9 V9;

            public Closure(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7, T8 v8, T9 v9)
            {
                V1 = v1;
                V2 = v2;
                V3 = v3;
                V4 = v4;
                V5 = v5;
                V6 = v6;
                V7 = v7;
                V8 = v8;
                V9 = v9;
            }
        }

        public sealed class Closure<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            public T1 V1;
            public T2 V2;
            public T3 V3;
            public T4 V4;
            public T5 V5;
            public T6 V6;
            public T7 V7;
            public T8 V8;
            public T9 V9;
            public T10 V10;

            public Closure(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7, T8 v8, T9 v9, T10 v10)
            {
                V1 = v1;
                V2 = v2;
                V3 = v3;
                V4 = v4;
                V5 = v5;
                V6 = v6;
                V7 = v7;
                V8 = v8;
                V9 = v9;
                V10 = v10;
            }
        }

        public sealed class ArrayClosure
        {
            public readonly object[] Constants;

            public static FieldInfo ArrayField = typeof(ArrayClosure).GetPublicInstanceField(nameof(Constants));
            public static ConstructorInfo Constructor = typeof(ArrayClosure).GetPublicInstanceConstructor();

            public ArrayClosure(object[] constants)
            {
                Constants = constants;
            }
        }

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

        #endregion

        #region Nested Lambdas

        private struct NestedLambdaInfo
        {
            public readonly ClosureInfo ClosureInfo;
            public readonly object Lambda;
            public readonly bool IsAction;

            public NestedLambdaInfo(ClosureInfo closureInfo, object lambda, bool isAction)
            {
                ClosureInfo = closureInfo;
                Lambda = lambda;
                IsAction = isAction;
            }
        }

        internal static class CurryClosureFuncs
        {
            public static readonly MethodInfo[] Methods = typeof(CurryClosureFuncs).GetPublicStaticMethods("Curry").ToArray();

            public static Func<R> Curry<C, R>(Func<C, R> f, C c) => () => f(c);
            public static Func<T1, R> Curry<C, T1, R>(Func<C, T1, R> f, C c) => t1 => f(c, t1);
            public static Func<T1, T2, R> Curry<C, T1, T2, R>(Func<C, T1, T2, R> f, C c) => (t1, t2) => f(c, t1, t2);

            public static Func<T1, T2, T3, R> Curry<C, T1, T2, T3, R>(Func<C, T1, T2, T3, R> f, C c) =>
                (t1, t2, t3) => f(c, t1, t2, t3);

            public static Func<T1, T2, T3, T4, R> Curry<C, T1, T2, T3, T4, R>(Func<C, T1, T2, T3, T4, R> f, C c) =>
                (t1, t2, t3, t4) => f(c, t1, t2, t3, t4);

            public static Func<T1, T2, T3, T4, T5, R> Curry<C, T1, T2, T3, T4, T5, R>(Func<C, T1, T2, T3, T4, T5, R> f,
                C c) => (t1, t2, t3, t4, t5) => f(c, t1, t2, t3, t4, t5);

            public static Func<T1, T2, T3, T4, T5, T6, R>
                Curry<C, T1, T2, T3, T4, T5, T6, R>(Func<C, T1, T2, T3, T4, T5, T6, R> f, C c) =>
                (t1, t2, t3, t4, t5, t6) => f(c, t1, t2, t3, t4, t5, t6);
        }

        internal static class CurryClosureActions
        {
            public static readonly MethodInfo[] Methods = typeof(CurryClosureActions).GetPublicStaticMethods("Curry").ToArray();

            internal static Action Curry<C>(Action<C> a, C c) => () => a(c);
            internal static Action<T1> Curry<C, T1>(Action<C, T1> f, C c) => t1 => f(c, t1);
            internal static Action<T1, T2> Curry<C, T1, T2>(Action<C, T1, T2> f, C c) => (t1, t2) => f(c, t1, t2);

            internal static Action<T1, T2, T3> Curry<C, T1, T2, T3>(Action<C, T1, T2, T3> f, C c) =>
                (t1, t2, t3) => f(c, t1, t2, t3);

            internal static Action<T1, T2, T3, T4> Curry<C, T1, T2, T3, T4>(Action<C, T1, T2, T3, T4> f, C c) =>
                (t1, t2, t3, t4) => f(c, t1, t2, t3, t4);

            internal static Action<T1, T2, T3, T4, T5> Curry<C, T1, T2, T3, T4, T5>(Action<C, T1, T2, T3, T4, T5> f,
                C c) => (t1, t2, t3, t4, t5) => f(c, t1, t2, t3, t4, t5);

            internal static Action<T1, T2, T3, T4, T5, T6>
                Curry<C, T1, T2, T3, T4, T5, T6>(Action<C, T1, T2, T3, T4, T5, T6> f, C c) =>
                (t1, t2, t3, t4, t5, t6) => f(c, t1, t2, t3, t4, t5, t6);
        }

        #endregion

        #region Collect Bound Constants

        private static bool IsClosureBoundConstant(object value, Type type)
        {
            return value is Delegate ||
                  !type.IsPrimitive() && !type.IsEnum() && !(value is string) && !(value is Type) && !(value is decimal);
        }

        // @paramExprs is required for nested lambda compilation
        private static bool TryCollectBoundConstants(ref ClosureInfo closure, Expression expr, IList<ParameterExpression> paramExprs)
        {
            while (true)
            {
                if (expr == null)
                {
                    return false;
                }

                switch (expr.NodeType)
                {
                    case ExpressionType.Constant:
                        var constantExpr = (ConstantExpression)expr;
                        var value = constantExpr.Value;
                        if (value != null && IsClosureBoundConstant(value, value.GetType()))
                        {
                            closure.AddConstant(constantExpr);
                        }

                        return true;

                    case ExpressionType.Parameter:
                        // if parameter is used BUT is not in passed parameters and not in local variables,
                        // it means parameter is provided by outer lambda and should be put in closure for current lambda
                        if (paramExprs.GetFirstIndex(expr) == -1 && !closure.IsLocalVar(expr))
                        {
                            closure.AddNonPassedParam((ParameterExpression)expr);
                        }

                        return true;

                    case ExpressionType.Call:
                        var methodCallExpr = (MethodCallExpression)expr;
                        if (methodCallExpr.Arguments.Count != 0 &&
                            !TryCollectBoundConstants(ref closure, methodCallExpr.Arguments, paramExprs))
                        {
                            return false;
                        }

                        if (methodCallExpr.Object == null)
                        {
                            return true;
                        }

                        expr = methodCallExpr.Object;
                        continue;

                    case ExpressionType.MemberAccess:
                        var memberExpr = ((MemberExpression)expr).Expression;
                        if (memberExpr == null)
                        {
                            return true;
                        }

                        expr = memberExpr;
                        continue;

                    case ExpressionType.New:
                        return TryCollectBoundConstants(ref closure, ((NewExpression)expr).Arguments, paramExprs);

                    case ExpressionType.NewArrayBounds:
                    case ExpressionType.NewArrayInit:
                        return TryCollectBoundConstants(ref closure, ((NewArrayExpression)expr).Expressions, paramExprs);

                    case ExpressionType.MemberInit:
                        return TryCollectMemberInitExprConstants(ref closure, (MemberInitExpression)expr, paramExprs);

                    case ExpressionType.Lambda:
                        closure.AddNestedLambda((LambdaExpression)expr);
                        return true;

                    case ExpressionType.Invoke:
                        // optimization #138: we are inlining invoked lambda body (only for lambdas without arguments)
                        // therefore we skipping collecting the lambda and invocation arguments and got directly to lambda body.
                        // This approach is repeated in `TryEmitInvoke`
                        var invokeExpr = (InvocationExpression)expr;
                        if (invokeExpr.Expression is LambdaExpression lambdaExpr && lambdaExpr.Parameters.Count == 0)
                        {
                            expr = lambdaExpr.Body;
                            continue;
                        }

                        if (invokeExpr.Arguments.Count != 0 &&
                            !TryCollectBoundConstants(ref closure, invokeExpr.Arguments, paramExprs))
                        {
                            return false;
                        }

                        expr = invokeExpr.Expression;
                        continue;

                    case ExpressionType.Conditional:
                        var condExpr = (ConditionalExpression)expr;
                        if (!TryCollectBoundConstants(ref closure, condExpr.Test, paramExprs) ||
                            !TryCollectBoundConstants(ref closure, condExpr.IfFalse, paramExprs))
                        {
                            return false;
                        }

                        expr = condExpr.IfTrue;
                        continue;

                    case ExpressionType.Block:
                        var blockExpr = (BlockExpression)expr;
                        closure.PushBlock(blockExpr.Variables, Enumerable<LocalBuilder>.EmptyArray);
                        if (!TryCollectBoundConstants(ref closure, blockExpr.Expressions, paramExprs))
                        {
                            return false;
                        }

                        closure.PopBlock();
                        return true;

                    case ExpressionType.Index:
                        var indexExpr = (IndexExpression)expr;
                        if (!TryCollectBoundConstants(ref closure, indexExpr.Arguments, paramExprs))
                        {
                            return false;
                        }

                        if (indexExpr.Object == null)
                        {
                            return true;
                        }

                        expr = indexExpr.Object;
                        continue;

                    case ExpressionType.Try:
                        return TryCollectTryExprConstants(ref closure, (TryExpression)expr, paramExprs);

                    case ExpressionType.Label:
                        closure.LabelCount += 1;
                        var defaultValueExpr = ((LabelExpression)expr).DefaultValue;
                        if (defaultValueExpr == null)
                        {
                            return true;
                        }

                        expr = defaultValueExpr;
                        continue;

                    case ExpressionType.Goto:
                        var gotoValueExpr = ((GotoExpression)expr).Value;
                        if (gotoValueExpr == null)
                        {
                            return true;
                        }

                        expr = gotoValueExpr;
                        continue;

                    case ExpressionType.Switch:
                        var switchExpr = ((SwitchExpression)expr);
                        if (!TryCollectBoundConstants(ref closure, switchExpr.SwitchValue, paramExprs) ||
                            switchExpr.DefaultBody != null && !TryCollectBoundConstants(ref closure, switchExpr.DefaultBody, paramExprs))
                        {
                            return false;
                        }

                        for (var i = 0; i < switchExpr.Cases.Count; i++)
                        {
                            if (!TryCollectBoundConstants(ref closure, switchExpr.Cases[i].Body, paramExprs))
                            {
                                return false;
                            }
                        }

                        return true;

                    case ExpressionType.Extension:
                        var reducedExpr = expr.Reduce();
                        if (closure.ReducedExpressions == null)
                        {
                            closure.ReducedExpressions = new Dictionary<Expression, Expression>();
                        }

                        closure.ReducedExpressions.Add(expr, reducedExpr);
                        expr = reducedExpr;
                        continue;

                    case ExpressionType.Default:
                        return true;

                    default:
                        if (expr is UnaryExpression unaryExpr)
                        {
                            expr = unaryExpr.Operand;
                            continue;
                        }

                        if (expr is BinaryExpression binaryExpr)
                        {
                            if (!TryCollectBoundConstants(ref closure, binaryExpr.Left, paramExprs))
                            {
                                return false;
                            }

                            expr = binaryExpr.Right;
                            continue;
                        }

                        if (expr is TypeBinaryExpression typeBinaryExpr)
                        {
                            expr = typeBinaryExpr.Expression;
                            continue;
                        }

                        return false;
                }
            }
        }

        private static bool TryCompileNestedLambda(ref ClosureInfo closure, int lambdaIndex,
            LambdaExpression lambdaExpr)
        {
            // 1. Try to compile nested lambda in place
            // 2. Check that parameters used in compiled lambda are passed or closed by outer lambda
            // 3. Add the compiled lambda to closure of outer lambda for later invocation

            var lambdaParamExprs = lambdaExpr.Parameters;

            var nestedClosure = new ClosureInfo(false);
            var compiledLambda = TryCompile(ref nestedClosure,
                lambdaExpr.Type, Tools.GetParamTypes(lambdaParamExprs), lambdaExpr.ReturnType, lambdaExpr.Body,
                lambdaParamExprs, isNestedLambda: true);

            if (compiledLambda == null)
            {
                return false;
            }

            var isAction = lambdaExpr.ReturnType == typeof(void);
            closure.NestedLambdas[lambdaIndex] = new NestedLambdaInfo(nestedClosure, compiledLambda, isAction);

            if (nestedClosure.HasClosure)
            {
                CopyNestedClosureInfo(lambdaParamExprs, ref closure, ref nestedClosure);
            }

            return true;
        }

        private static bool TryCollectMemberInitExprConstants(ref ClosureInfo closure, MemberInitExpression expr,
            IList<ParameterExpression> paramExprs)
        {
            var newExpr = expr.NewExpression
#if LIGHT_EXPRESSION
                          ?? expr.Expression
#endif
                ;
            if (!TryCollectBoundConstants(ref closure, newExpr, paramExprs))
            {
                return false;
            }

            var memberBindings = expr.Bindings;
            for (var i = 0; i < memberBindings.Count; ++i)
            {
                var memberBinding = memberBindings[i];
                if (memberBinding.BindingType == MemberBindingType.Assignment &&
                    !TryCollectBoundConstants(ref closure, ((MemberAssignment)memberBinding).Expression, paramExprs))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryCollectTryExprConstants(
            ref ClosureInfo closure,
            TryExpression tryExpr,
            IList<ParameterExpression> paramExprs)
        {
            if (!TryCollectBoundConstants(ref closure, tryExpr.Body, paramExprs))
            {
                return false;
            }

            var catchBlocks = tryExpr.Handlers;
            for (var i = 0; i < catchBlocks.Count; i++)
            {
                var catchBlock = catchBlocks[i];
                var catchBody = catchBlock.Body;
                var catchExVar = catchBlock.Variable;
                if (catchExVar != null)
                {
                    closure.PushBlock(new[] { catchExVar }, Enumerable<LocalBuilder>.EmptyArray);
                    if (!TryCollectBoundConstants(ref closure, catchExVar, paramExprs))
                    {
                        return false;
                    }
                }

                var filterExpr = catchBlock.Filter;
                if (filterExpr != null &&
                    !TryCollectBoundConstants(ref closure, filterExpr, paramExprs) ||
                    !TryCollectBoundConstants(ref closure, catchBody, paramExprs))
                {
                    return false;
                }

                if (catchExVar != null)
                {
                    closure.PopBlock();
                }
            }

            var finallyExpr = tryExpr.Finally;
            return finallyExpr == null || TryCollectBoundConstants(ref closure, finallyExpr, paramExprs);
        }

        private static bool TryCollectBoundConstants(
            ref ClosureInfo closure,
            IList<Expression> exprs,
            IList<ParameterExpression> paramExprs)
        {
            for (var i = 0; i < exprs.Count; i++)
            {
                if (!TryCollectBoundConstants(ref closure, exprs[i], paramExprs))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        // The minimal context-aware flags set by parent
        [Flags]
        internal enum ParentFlags
        {
            Empty = 0,
            IgnoreResult = 1 << 1,
            Call = 1 << 2,
            MemberAccess = 1 << 3, // Any Parent Expression is a MemberExpression
            Arithmetic = 1 << 4,
            Coalesce = 1 << 5,
            InstanceAccess = 1 << 6
        }

        internal static bool ShouldIgnoreResult(ParentFlags parent) => (parent & ParentFlags.IgnoreResult) != 0;

        /// <summary>Supports emitting of selected expressions, e.g. lambdaExpr are not supported yet.
        /// When emitter find not supported expression it will return false from <see cref="TryEmit"/>, so I could fallback
        /// to normal and slow Expression.Compile.</summary>
        private static class EmittingVisitor
        {
#if !NETSTANDARD2_0 && !NET45
            private static readonly MethodInfo _getTypeFromHandleMethod = typeof(Type).GetPublicStaticMethod("GetTypeFromHandle");
            private static readonly MethodInfo _objectEqualsMethod = typeof(object).GetPublicStaticMethod("Equals");
#else
            private static readonly MethodInfo _getTypeFromHandleMethod =
                ((Func<RuntimeTypeHandle, Type>)Type.GetTypeFromHandle).Method;

            private static readonly MethodInfo _objectEqualsMethod = ((Func<object, object, bool>)object.Equals).Method;
#endif

            public static bool TryEmit(
                Expression expr,
                IList<ParameterExpression> paramExprs,
                ILGenerator il,
                ref ClosureInfo closure,
                ParentFlags parent,
                int byRefIndex = -1)
            {
                while (true)
                {
                    closure.LastEmitIsAddress = false;

                    switch (expr.NodeType)
                    {
                        case ExpressionType.Parameter:
                            return ShouldIgnoreResult(parent) ||
                                   TryEmitParameter((ParameterExpression)expr, paramExprs, il, ref closure, parent,
                                       byRefIndex);
                        case ExpressionType.TypeAs:
                            return TryEmitTypeAs((UnaryExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.TypeIs:
                            return TryEmitTypeIs((TypeBinaryExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.Not:
                            return TryEmitNot((UnaryExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.Convert:
                        case ExpressionType.ConvertChecked:
                            return TryEmitConvert((UnaryExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.ArrayIndex:
                            var arrIndexExpr = (BinaryExpression)expr;
                            return TryEmit(arrIndexExpr.Left, paramExprs, il, ref closure, parent) &&
                                   TryEmit(arrIndexExpr.Right, paramExprs, il, ref closure, parent) &&
                                   TryEmitArrayIndex(expr.Type, il);

                        case ExpressionType.Constant:
                            var constantExpression = (ConstantExpression)expr;
                            return ShouldIgnoreResult(parent) ||
                                   TryEmitConstant(constantExpression, constantExpression.Type, constantExpression.Value, il, ref closure);

                        case ExpressionType.Call:
                            return TryEmitMethodCall((MethodCallExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.MemberAccess:
                            return TryEmitMemberAccess((MemberExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.New:
                            var newExpr = (NewExpression)expr;
                            var argExprs = newExpr.Arguments;
                            for (var i = 0; i < argExprs.Count; i++)
                            {
                                var idx = argExprs[i].Type.IsByRef ? i : -1;
                                if (!TryEmit(argExprs[i], paramExprs, il, ref closure, parent, idx))
                                {
                                    return false;
                                }
                            }
                            return TryEmitNew(newExpr.Constructor, newExpr.Type, il);

                        case ExpressionType.NewArrayBounds:
                        case ExpressionType.NewArrayInit:
                            return EmitNewArray((NewArrayExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.MemberInit:
                            return EmitMemberInit((MemberInitExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.Lambda:
                            return TryEmitNestedLambda((LambdaExpression)expr, paramExprs, il, ref closure);

                        case ExpressionType.Invoke:
                            return TryEmitInvoke((InvocationExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.GreaterThan:
                        case ExpressionType.GreaterThanOrEqual:
                        case ExpressionType.LessThan:
                        case ExpressionType.LessThanOrEqual:
                        case ExpressionType.Equal:
                        case ExpressionType.NotEqual:
                            var binaryExpr = (BinaryExpression)expr;
                            return TryEmitComparison(binaryExpr.Left, binaryExpr.Right, binaryExpr.NodeType,
                                paramExprs, il, ref closure, parent);

                        case ExpressionType.Add:
                        case ExpressionType.AddChecked:
                        case ExpressionType.Subtract:
                        case ExpressionType.SubtractChecked:
                        case ExpressionType.Multiply:
                        case ExpressionType.MultiplyChecked:
                        case ExpressionType.Divide:
                        case ExpressionType.Modulo:
                        case ExpressionType.Power:
                        case ExpressionType.And:
                        case ExpressionType.Or:
                        case ExpressionType.ExclusiveOr:
                        case ExpressionType.LeftShift:
                        case ExpressionType.RightShift:
                            var arithmeticExpr = (BinaryExpression)expr;
                            return
                                TryEmit(arithmeticExpr.Left, paramExprs, il, ref closure,
                                    (parent | ParentFlags.Arithmetic) & ~ParentFlags.InstanceAccess) &&
                                TryEmit(arithmeticExpr.Right, paramExprs, il, ref closure,
                                    (parent | ParentFlags.Arithmetic) & ~ParentFlags.InstanceAccess) &&
                                TryEmitArithmeticOperation(expr.NodeType, expr.Type, il);

                        case ExpressionType.AndAlso:
                        case ExpressionType.OrElse:
                            return TryEmitLogicalOperator((BinaryExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.Coalesce:
                            return TryEmitCoalesceOperator((BinaryExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.Conditional:
                            return TryEmitConditional((ConditionalExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.PostIncrementAssign:
                        case ExpressionType.PreIncrementAssign:
                        case ExpressionType.PostDecrementAssign:
                        case ExpressionType.PreDecrementAssign:
                            return TryEmitIncDecAssign((UnaryExpression)expr, il, ref closure, parent);

                        case ExpressionType arithmeticAssign
                            when Tools.GetArithmeticFromArithmeticAssignOrSelf(arithmeticAssign) != arithmeticAssign:
                        case ExpressionType.Assign:
                            return TryEmitAssign((BinaryExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.Block:
                            var blockExpr = (BlockExpression)expr;
                            var blockHasVars = blockExpr.Variables.Count != 0;
                            if (blockHasVars)
                            {
                                closure.PushBlockAndConstructLocalVars(blockExpr.Variables, il);
                            }

                            // ignore result for all not the last statements in block
                            var exprs = blockExpr.Expressions;
                            for (var i = 0; i < exprs.Count - 1; i++)
                            {
                                if (!TryEmit(exprs[i], paramExprs, il, ref closure, parent | ParentFlags.IgnoreResult))
                                {
                                    return false;
                                }
                            }

                            // last (result) statement in block will provide the result
                            expr = blockExpr.Result;
                            if (!blockHasVars)
                            {
                                continue; // omg, no recursion!
                            }

                            if (!TryEmit(blockExpr.Result, paramExprs, il, ref closure, parent))
                            {
                                return false;
                            }

                            closure.PopBlock();
                            return true;

                        case ExpressionType.Try:
                            return TryEmitTryCatchFinallyBlock((TryExpression)expr, paramExprs, il, ref closure,
                                parent);

                        case ExpressionType.Throw:
                            {
                                var opExpr = ((UnaryExpression)expr).Operand;
                                if (!TryEmit(opExpr, paramExprs, il, ref closure, parent))
                                {
                                    return false;
                                }

                                il.ThrowException(opExpr.Type);
                                return true;
                            }

                        case ExpressionType.Default:
                            return expr.Type == typeof(void) || ShouldIgnoreResult(parent) ||
                                   EmitDefault(expr.Type, il);

                        case ExpressionType.Index:
                            var indexExpr = (IndexExpression)expr;
                            if (indexExpr.Object != null &&
                                !TryEmit(indexExpr.Object, paramExprs, il, ref closure, parent))
                            {
                                return false;
                            }

                            var indexArgExprs = indexExpr.Arguments;
                            for (var i = 0; i < indexArgExprs.Count; i++)
                            {
                                var idx = indexArgExprs[i].Type.IsByRef ? i : -1;
                                if (!TryEmit(indexArgExprs[i], paramExprs, il, ref closure, parent, idx))
                                {
                                    return false;
                                }
                            }

                            return TryEmitIndex((IndexExpression)expr, il);

                        case ExpressionType.Goto:
                            return TryEmitGoto((GotoExpression)expr, il, ref closure);

                        case ExpressionType.Label:
                            return TryEmitLabel((LabelExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.Switch:
                            return TryEmitSwitch((SwitchExpression)expr, paramExprs, il, ref closure, parent);

                        case ExpressionType.Extension:
                            expr = closure.ReducedExpressions[expr];
                            continue;

                        default:
                            return false;
                    }
                }
            }

            private static bool TryEmitLabel(
                LabelExpression expr,
                IList<ParameterExpression> paramExprs,
                ILGenerator il,
                ref ClosureInfo closure,
                ParentFlags parent)
            {
                var lbl = closure.Labels.FirstOrDefault(x => x.Key == expr.Target);
                if (lbl.Key != expr.Target)
                {
                    closure.Labels[closure.LabelCount++] = lbl = new KeyValuePair<object, Label>(expr.Target, il.DefineLabel());
                }

                il.MarkLabel(lbl.Value);

                return expr.DefaultValue == null || TryEmit(expr.DefaultValue, paramExprs, il, ref closure, parent);
            }

            // todo: GotoExpression.Value 
            private static bool TryEmitGoto(GotoExpression exprObj, ILGenerator il, ref ClosureInfo closure)
            {
                var labels = closure.Labels;
                if (labels == null)
                {
                    throw new InvalidOperationException("Cannot jump, no labels found");
                }

                var lbl = labels.FirstOrDefault(x => x.Key == exprObj.Target);
                if (lbl.Key != exprObj.Target)
                {
                    if (labels.Length == closure.LabelCount - 1)
                    {
                        throw new InvalidOperationException("Cannot jump, not all labels found");
                    }

                    lbl = new KeyValuePair<object, Label>(exprObj.Target, il.DefineLabel());
                    labels[closure.LabelCount++] = lbl;
                }

                if (exprObj.Kind == GotoExpressionKind.Goto)
                {
                    il.Emit(OpCodes.Br, lbl.Value);
                    return true;
                }

                return false;
            }

            private static bool TryEmitIndex(IndexExpression expr, ILGenerator il)
            {
                var elemType = expr.Type;
                if (expr.Indexer != null)
                {
                    return EmitMethodCall(il, expr.Indexer.GetGetter());
                }

                if (expr.Arguments.Count == 1) // one dimensional array
                {
                    if (elemType.IsValueType())
                    {
                        il.Emit(OpCodes.Ldelem, elemType);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldelem_Ref);
                    }

                    return true;
                }

                // multi dimensional array
                return EmitMethodCall(il, expr.Object?.Type.GetPublicInstanceMethod("Get"));
            }

            private static bool TryEmitCoalesceOperator(BinaryExpression exprObj,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                var labelFalse = il.DefineLabel();
                var labelDone = il.DefineLabel();

                var left = exprObj.Left;
                var right = exprObj.Right;

                if (!TryEmit(left, paramExprs, il, ref closure, parent | ParentFlags.Coalesce))
                {
                    return false;
                }

                var leftType = left.Type;
                if (leftType.IsValueType()) // Nullable -> It's the only ValueType comparable to null
                {
                    var loc = il.DeclareLocal(leftType);
                    il.Emit(OpCodes.Stloc_S, loc);
                    il.Emit(OpCodes.Ldloca_S, loc);

                    if (!EmitMethodCall(il, leftType.FindNullableHasValueGetterMethod()))
                    {
                        return false;
                    }

                    il.Emit(OpCodes.Brfalse, labelFalse);
                    il.Emit(OpCodes.Ldloca_S, loc);
                    if (!EmitMethodCall(il, leftType.FindNullableHasValueGetterMethod()))
                    {
                        return false;
                    }

                    il.Emit(OpCodes.Br, labelDone);
                    il.MarkLabel(labelFalse);
                    if (!TryEmit(right, paramExprs, il, ref closure, parent | ParentFlags.Coalesce))
                    {
                        return false;
                    }

                    il.MarkLabel(labelDone);
                    return true;
                }

                il.Emit(OpCodes.Dup); // duplicate left, if it's not null, after the branch this value will be on the top of the stack
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Brfalse, labelFalse);

                il.Emit(OpCodes.Pop); // left is null, pop its value from the stack

                if (!TryEmit(right, paramExprs, il, ref closure, parent | ParentFlags.Coalesce))
                {
                    return false;
                }

                if (right.Type != exprObj.Type)
                {
                    if (right.Type.IsValueType())
                    {
                        il.Emit(OpCodes.Box, right.Type);
                    }
                    else
                    {
                        il.Emit(OpCodes.Castclass, exprObj.Type);
                    }
                }

                if (left.Type == exprObj.Type)
                {
                    il.MarkLabel(labelFalse);
                }
                else
                {
                    il.Emit(OpCodes.Br, labelDone);
                    il.MarkLabel(labelFalse);
                    il.Emit(OpCodes.Castclass, exprObj.Type);
                    il.MarkLabel(labelDone);
                }

                return true;
            }

            private static bool EmitDefault(Type type, ILGenerator il)
            {
                if (type == typeof(string))
                {
                    il.Emit(OpCodes.Ldnull);
                }
                else if (
                    type == typeof(bool) ||
                    type == typeof(byte) ||
                    type == typeof(char) ||
                    type == typeof(sbyte) ||
                    type == typeof(int) ||
                    type == typeof(uint) ||
                    type == typeof(short) ||
                    type == typeof(ushort))
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                }
                else if (
                    type == typeof(long) ||
                    type == typeof(ulong))
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Conv_I8);
                }
                else if (type == typeof(float))
                {
                    il.Emit(OpCodes.Ldc_R4, default(float));
                }
                else if (type == typeof(double))
                {
                    il.Emit(OpCodes.Ldc_R8, default(double));
                }
                else if (type.IsValueType())
                {
                    il.Emit(OpCodes.Ldloc, InitValueTypeVariable(il, type));
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                return true;
            }


            private static bool TryEmitTryCatchFinallyBlock(TryExpression tryExpr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                var exprType = tryExpr.Type;
                var returnLabel = default(Label);
                var returnResult = default(LocalBuilder);
                var isNonVoid = exprType != typeof(void); // todo: check how it is correlated with `parent.IgnoreResult`
                if (isNonVoid)
                {
                    returnLabel = il.DefineLabel();
                    returnResult = il.DeclareLocal(exprType);
                }

                il.BeginExceptionBlock();
                var tryBodyExpr = tryExpr.Body;
                if (!TryEmit(tryBodyExpr, paramExprs, il, ref closure, parent))
                {
                    return false;
                }

                if (isNonVoid)
                {
                    il.Emit(OpCodes.Stloc_S, returnResult);
                    il.Emit(OpCodes.Leave_S, returnLabel);
                }

                var catchBlocks = tryExpr.Handlers;
                for (var i = 0; i < catchBlocks.Count; i++)
                {
                    var catchBlock = catchBlocks[i];
                    if (catchBlock.Filter != null)
                    {
                        return false; // todo: Add support for filters on catch expression
                    }

                    il.BeginCatchBlock(catchBlock.Test);

                    // at the beginning of catch the Exception value is on the stack,
                    // we will store into local variable.
                    var exVarExpr = catchBlock.Variable;
                    if (exVarExpr != null)
                    {
                        var exVar = il.DeclareLocal(exVarExpr.Type);
                        closure.PushBlock(new[] { exVarExpr }, new[] { exVar });
                        il.Emit(OpCodes.Stloc_S, exVar);
                    }

                    var catchBodyExpr = catchBlock.Body;
                    if (!TryEmit(catchBodyExpr, paramExprs, il, ref closure, parent))
                    {
                        return false;
                    }

                    if (exVarExpr != null)
                    {
                        closure.PopBlock();
                    }

                    if (isNonVoid)
                    {
                        il.Emit(OpCodes.Stloc_S, returnResult);
                        il.Emit(OpCodes.Leave_S, returnLabel);
                    }
                    else
                    {
                        if (catchBodyExpr.Type != typeof(void))
                        {
                            il.Emit(OpCodes.Pop);
                        }
                    }
                }

                var finallyExpr = tryExpr.Finally;
                if (finallyExpr != null)
                {
                    il.BeginFinallyBlock();
                    if (!TryEmit(finallyExpr, paramExprs, il, ref closure, parent))
                    {
                        return false;
                    }
                }

                il.EndExceptionBlock();
                if (isNonVoid)
                {
                    il.MarkLabel(returnLabel);
                    il.Emit(OpCodes.Ldloc, returnResult);
                }

                return true;
            }

            private static bool TryEmitParameter(
                ParameterExpression paramExpr,
                IList<ParameterExpression> paramExprs,
                ILGenerator il,
                ref ClosureInfo closure,
                ParentFlags parent,
                int byRefIndex = -1)
            {
                var paramType = paramExpr.Type;

                // if parameter is passed through, then just load it on stack
                var paramIndex = paramExprs.GetFirstIndex(paramExpr);
                if (paramIndex != -1)
                {
                    if (closure.HasClosure)
                    {
                        paramIndex += 1; // shift parameter indices by one, because the first one will be closure
                    }

                    var asAddress =
                        paramType.IsValueType() && !paramExpr.IsByRef &&
                      ((parent & (ParentFlags.Call | ParentFlags.InstanceAccess)) == (ParentFlags.Call | ParentFlags.InstanceAccess) ||
                       (parent & ParentFlags.MemberAccess) != 0);

                    EmitLoadParamArg(il, paramIndex, asAddress);

                    if (paramExpr.IsByRef)
                    {
                        if ((parent & ParentFlags.Coalesce) != 0)
                        {
                            il.Emit(OpCodes.Ldind_Ref); // Coalesce on for ref types
                        }
                        else if ((parent & ParentFlags.Arithmetic) != 0)
                        {
                            EmitDereference(il, paramType);
                        }
                    }

                    return true;
                }

                // if parameter isn't passed, then it is passed into some outer lambda or it is a local variable,
                // so it should be loaded from closure or from the locals. Then the closure is null will be an invalid state.
                if (!closure.IsClosureConstructed)
                {
                    return false;
                }

                // parameter may represent a variable, so first look if this is the case
                var variable = closure.GetDefinedLocalVarOrDefault(paramExpr);
                if (variable != null)
                {
                    if (byRefIndex != -1 || (paramType.IsValueType() && (parent & ParentFlags.MemberAccess) != 0))
                    {
                        il.Emit(OpCodes.Ldloca_S, variable);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldloc, variable);
                    }

                    return true;
                }

                if (paramExpr.IsByRef)
                {
                    il.Emit(OpCodes.Ldloca_S, byRefIndex);
                    return true;
                }

                // the only possibility that we are here is because we are in nested lambda,
                // and it uses some parameter or variable from the outer lambda
                var nonPassedParamIndex = closure.NonPassedParameters.GetFirstIndex(paramExpr);
                if (nonPassedParamIndex == -1)
                {
                    return false; // what??? no chance
                }

                var closureItemIndex = closure.Constants.Length + nonPassedParamIndex;
                return LoadClosureFieldOrItem(ref closure, il, closureItemIndex, paramType);
            }

            private static void EmitDereference(ILGenerator il, Type type)
            {
                if (type == typeof(Int32))
                {
                    il.Emit(OpCodes.Ldind_I4);
                }
                else if (type == typeof(Int64))
                {
                    il.Emit(OpCodes.Ldind_I8);
                }
                else if (type == typeof(Int16))
                {
                    il.Emit(OpCodes.Ldind_I2);
                }
                else if (type == typeof(SByte))
                {
                    il.Emit(OpCodes.Ldind_I1);
                }
                else if (type == typeof(Single))
                {
                    il.Emit(OpCodes.Ldind_R4);
                }
                else if (type == typeof(Double))
                {
                    il.Emit(OpCodes.Ldind_R8);
                }
                else if (type == typeof(IntPtr))
                {
                    il.Emit(OpCodes.Ldind_I);
                }
                else if (type == typeof(UIntPtr))
                {
                    il.Emit(OpCodes.Ldind_I);
                }
                else if (type == typeof(Byte))
                {
                    il.Emit(OpCodes.Ldind_U1);
                }
                else if (type == typeof(UInt16))
                {
                    il.Emit(OpCodes.Ldind_U2);
                }
                else if (type == typeof(UInt32))
                {
                    il.Emit(OpCodes.Ldind_U4);
                }
                else
                {
                    il.Emit(OpCodes.Ldobj, type);
                }
                //todo: UInt64 as there is no OpCodes? Ldind_Ref?
            }

            // loads argument at paramIndex onto evaluation stack
            private static void EmitLoadParamArg(ILGenerator il, int paramIndex, bool asAddress)
            {
                if (asAddress)
                {
                    if (paramIndex <= byte.MaxValue)
                    {
                        il.Emit(OpCodes.Ldarga_S, (byte)paramIndex);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldarga, paramIndex);
                    }

                    return;
                }

                switch (paramIndex)
                {
                    case 0:
                        il.Emit(OpCodes.Ldarg_0);
                        break;
                    case 1:
                        il.Emit(OpCodes.Ldarg_1);
                        break;
                    case 2:
                        il.Emit(OpCodes.Ldarg_2);
                        break;
                    case 3:
                        il.Emit(OpCodes.Ldarg_3);
                        break;
                    default:
                        if (paramIndex <= byte.MaxValue)
                        {
                            il.Emit(OpCodes.Ldarg_S, (byte)paramIndex);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldarg, paramIndex);
                        }

                        break;
                }
            }

            private static bool TryEmitTypeAs(UnaryExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure,
                ParentFlags parent)
            {
                if (!TryEmit(expr.Operand, paramExprs, il, ref closure, parent))
                {
                    return false;
                }

                if ((parent & ParentFlags.IgnoreResult) > 0)
                {
                    il.Emit(OpCodes.Pop);
                }
                else
                {
                    il.Emit(OpCodes.Isinst, expr.Type);
                }
                return true;
            }
            private static bool TryEmitTypeIs(TypeBinaryExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure,
                ParentFlags parent)
            {
                if (!TryEmit(expr.Expression, paramExprs, il, ref closure, parent))
                {
                    return false;
                }

                if ((parent & ParentFlags.IgnoreResult) > 0)
                {
                    il.Emit(OpCodes.Pop);
                }
                else
                {
                    il.Emit(OpCodes.Isinst, expr.TypeOperand);
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Cgt_Un);
                }
                return true;
            }

            private static bool TryEmitNot(UnaryExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure,
                ParentFlags parent)
            {
                if (!TryEmit(expr.Operand, paramExprs, il, ref closure, parent))
                {
                    return false;
                }

                if ((parent & ParentFlags.IgnoreResult) > 0)
                {
                    il.Emit(OpCodes.Pop);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                }
                return true;
            }

            private static bool TryEmitConvert(UnaryExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                var targetType = expr.Type;
                var opExpr = expr.Operand;
                var method = expr.Method;
                if (method != null && method.Name != "op_Implicit" && method.Name != "op_Explicit")
                {
                    return TryEmit(opExpr, paramExprs, il, ref closure, parent & ~ParentFlags.IgnoreResult | ParentFlags.Call | ParentFlags.InstanceAccess, 0)
                        && EmitMethodCall(il, method, parent);
                }

                if (!TryEmit(opExpr, paramExprs, il, ref closure, parent & ~ParentFlags.IgnoreResult))
                {
                    return false;
                }

                var sourceType = opExpr.Type;
                if (sourceType == targetType || targetType == typeof(object))
                {
                    if (targetType == typeof(object) && sourceType.IsValueType())
                    {
                        il.Emit(OpCodes.Box, sourceType);
                    }

                    if (ShouldIgnoreResult(parent))
                    {
                        il.Emit(OpCodes.Pop);
                    }

                    return true;
                }

                // check implicit / explicit conversion operators on source and target types - #73
                if (!sourceType.IsPrimitive())
                {
                    var convertOpMethod = FirstConvertOperatorOrDefault(sourceType, targetType, sourceType);
                    if (convertOpMethod != null)
                    {
                        return EmitMethodCall(il, convertOpMethod, parent);
                    }
                }

                if (!targetType.IsPrimitive())
                {
                    var convertOpMethod = FirstConvertOperatorOrDefault(targetType, targetType, sourceType);
                    if (convertOpMethod != null)
                    {
                        return EmitMethodCall(il, convertOpMethod, parent);
                    }
                }

                if (sourceType == typeof(object) && targetType.IsValueType())
                {
                    il.Emit(OpCodes.Unbox_Any, targetType);
                }

                // Conversion to Nullable: new Nullable<T>(T val);
                else if (targetType.IsNullable())
                {
                    if (sourceType.IsNullable())
                    {
                        var labelFalse = il.DefineLabel();
                        var labelDone = il.DefineLabel();
                        var loc = il.DeclareLocal(sourceType);
                        var locT = il.DeclareLocal(targetType);
                        il.Emit(OpCodes.Stloc_S, loc);
                        il.Emit(OpCodes.Ldloca_S, loc);
                        if (!EmitMethodCall(il, sourceType.FindNullableHasValueGetterMethod()))
                        {
                            return false;
                        }

                        il.Emit(OpCodes.Brfalse, labelFalse);
                        il.Emit(OpCodes.Ldloca_S, loc);
                        if (!EmitMethodCall(il, sourceType.FindNullableValueOrDefaultMethod()))
                        {
                            return false;
                        }

                        TryEmitValueConvert(Nullable.GetUnderlyingType(targetType), il,
                            expr.NodeType == ExpressionType.ConvertChecked);
                        il.Emit(OpCodes.Newobj, targetType.GetPublicInstanceConstructor(targetType.GetGenericTypeArguments()[0]));
                        il.Emit(OpCodes.Stloc_S, locT);
                        il.Emit(OpCodes.Br_S, labelDone);
                        il.MarkLabel(labelFalse);
                        il.Emit(OpCodes.Ldloca_S, locT);
                        il.Emit(OpCodes.Initobj, targetType);
                        il.MarkLabel(labelDone);
                        il.Emit(OpCodes.Ldloc_S, locT);
                        if ((parent & ParentFlags.IgnoreResult) > 0)
                        {
                            il.Emit(OpCodes.Pop);
                        }

                        return true;
                    }

                    il.Emit(OpCodes.Newobj, targetType.GetPublicInstanceConstructor(targetType.GetGenericTypeArguments()[0]));
                }
                else
                {
                    if (targetType.IsEnum())
                    {
                        targetType = Enum.GetUnderlyingType(targetType);
                    }

                    // cast as the last resort and let's it fail if unlucky
                    if (!TryEmitValueConvert(targetType, il, expr.NodeType == ExpressionType.ConvertChecked))
                    {
                        il.Emit(OpCodes.Castclass, targetType);
                    }
                }

                if (ShouldIgnoreResult(parent))
                {
                    il.Emit(OpCodes.Pop);
                }

                return true;
            }

            private static bool TryEmitValueConvert(Type targetType, ILGenerator il, bool @checked)
            {
                if (targetType == typeof(int))
                {
                    il.Emit(@checked ? OpCodes.Conv_Ovf_I4 : OpCodes.Conv_I4);
                }
                else if (targetType == typeof(float))
                {
                    il.Emit(OpCodes.Conv_R4);
                }
                else if (targetType == typeof(uint))
                {
                    il.Emit(@checked ? OpCodes.Conv_Ovf_U4 : OpCodes.Conv_U4);
                }
                else if (targetType == typeof(sbyte))
                {
                    il.Emit(@checked ? OpCodes.Conv_Ovf_I1 : OpCodes.Conv_I1);
                }
                else if (targetType == typeof(byte))
                {
                    il.Emit(@checked ? OpCodes.Conv_Ovf_U1 : OpCodes.Conv_U1);
                }
                else if (targetType == typeof(short))
                {
                    il.Emit(@checked ? OpCodes.Conv_Ovf_I2 : OpCodes.Conv_I2);
                }
                else if (targetType == typeof(ushort) || targetType == typeof(char))
                {
                    il.Emit(@checked ? OpCodes.Conv_Ovf_U2 : OpCodes.Conv_U2);
                }
                else if (targetType == typeof(long))
                {
                    il.Emit(@checked ? OpCodes.Conv_Ovf_I8 : OpCodes.Conv_I8);
                }
                else if (targetType == typeof(ulong))
                {
                    il.Emit(@checked ? OpCodes.Conv_Ovf_U8 : OpCodes.Conv_U8);
                }
                else if (targetType == typeof(double))
                {
                    il.Emit(OpCodes.Conv_R8);
                }
                else
                {
                    return false;
                }

                return true;
            }

            private static MethodInfo FirstConvertOperatorOrDefault(Type type, Type targetType, Type sourceType)
                => type.GetOperators().GetFirst(m => m.ReturnType == targetType && m.GetParameters()[0].ParameterType == sourceType);

            private static bool TryEmitConstant(ConstantExpression expr, Type exprType, object constantValue, ILGenerator il, ref ClosureInfo closure)
            {
                if (constantValue == null)
                {
                    if (exprType.IsValueType()) // handles the conversion of null to Nullable<T>
                    {
                        il.Emit(OpCodes.Ldloc, InitValueTypeVariable(il, exprType));
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldnull);
                    }

                    return true;
                }

                var constantType = constantValue.GetType();
                if (expr != null && IsClosureBoundConstant(constantValue, constantType))
                {
                    var constIndex = closure.Constants.GetFirstIndex(expr);
                    if (constIndex == -1 || !LoadClosureFieldOrItem(ref closure, il, constIndex, exprType))
                    {
                        return false;
                    }
                }
                else
                {
                    // get raw enum type to light
                    if (constantType.IsEnum())
                    {
                        constantType = Enum.GetUnderlyingType(constantType);
                    }

                    if (constantType == typeof(int))
                    {
                        EmitLoadConstantInt(il, (int)constantValue);
                    }
                    else if (constantType == typeof(char))
                    {
                        EmitLoadConstantInt(il, (char)constantValue);
                    }
                    else if (constantType == typeof(short))
                    {
                        EmitLoadConstantInt(il, (short)constantValue);
                    }
                    else if (constantType == typeof(byte))
                    {
                        EmitLoadConstantInt(il, (byte)constantValue);
                    }
                    else if (constantType == typeof(ushort))
                    {
                        EmitLoadConstantInt(il, (ushort)constantValue);
                    }
                    else if (constantType == typeof(sbyte))
                    {
                        EmitLoadConstantInt(il, (sbyte)constantValue);
                    }
                    else if (constantType == typeof(uint))
                    {
                        unchecked
                        {
                            EmitLoadConstantInt(il, (int)(uint)constantValue);
                        }
                    }
                    else if (constantType == typeof(long))
                    {
                        il.Emit(OpCodes.Ldc_I8, (long)constantValue);
                    }
                    else if (constantType == typeof(ulong))
                    {
                        unchecked
                        {
                            il.Emit(OpCodes.Ldc_I8, (long)(ulong)constantValue);
                        }
                    }
                    else if (constantType == typeof(float))
                    {
                        il.Emit(OpCodes.Ldc_R4, (float)constantValue);
                    }
                    else if (constantType == typeof(double))
                    {
                        il.Emit(OpCodes.Ldc_R8, (double)constantValue);
                    }
                    else if (constantType == typeof(bool))
                    {
                        il.Emit((bool)constantValue ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                    }
                    else if (constantValue is string)
                    {
                        il.Emit(OpCodes.Ldstr, (string)constantValue);
                    }
                    else if (constantValue is Type)
                    {
                        il.Emit(OpCodes.Ldtoken, (Type)constantValue);
                        il.Emit(OpCodes.Call, _getTypeFromHandleMethod);
                    }
                    else if (constantType == typeof(IntPtr))
                    {
                        il.Emit(OpCodes.Ldc_I8, ((IntPtr)constantValue).ToInt64());
                    }
                    else if (constantType == typeof(UIntPtr))
                    {
                        unchecked
                        {
                            il.Emit(OpCodes.Ldc_I8, (long)((UIntPtr)constantValue).ToUInt64());
                        }
                    }
                    else if (constantType == typeof(decimal))
                    {
                        //check if decimal has decimal places, if not use shorter IL code (constructor from int or long)
                        var value = (decimal)constantValue;
                        if (value % 1 == 0)
                        {
                            if (value <= int.MaxValue && value >= int.MinValue)
                            {
                                EmitLoadConstantInt(il, decimal.ToInt32(value));
                                il.Emit(OpCodes.Newobj, typeof(decimal).GetPublicInstanceConstructor(typeof(int)));
                            }
                            else if (value <= long.MaxValue && value >= long.MinValue)
                            {
                                il.Emit(OpCodes.Ldc_I8, decimal.ToInt64(value));
                                il.Emit(OpCodes.Newobj, typeof(decimal).GetPublicInstanceConstructor(typeof(int)));
                            }
                        }
                        else
                        {
                            int[] parts = Decimal.GetBits(value);
                            bool sign = (parts[3] & 0x80000000) != 0;
                            byte scale = (byte)((parts[3] >> 16) & 0x7F);
                            EmitLoadConstantInt(il, parts[0]);
                            EmitLoadConstantInt(il, parts[1]);
                            EmitLoadConstantInt(il, parts[2]);
                            il.Emit(sign ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                            EmitLoadConstantInt(il, scale);
                            il.Emit(OpCodes.Conv_U1);
                            il.Emit(OpCodes.Newobj,
                                typeof(decimal).GetPublicInstanceConstructors().First(x =>
                                    x.GetParameters().Length == 5));
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                var underlyingNullableType = Nullable.GetUnderlyingType(exprType);
                if (underlyingNullableType != null)
                {
                    il.Emit(OpCodes.Newobj, exprType.GetPublicInstanceConstructor());
                }

                // todo: consider how to remove boxing where it is not required
                // boxing the value type, otherwise we can get a strange result when 0 is treated as Null.
                else if (exprType == typeof(object) && constantType.IsValueType())
                {
                    il.Emit(OpCodes.Box, constantValue.GetType()); // using normal type for Enum instead of underlying type
                }

                return true;
            }

            private static LocalBuilder InitValueTypeVariable(ILGenerator il, Type exprType,
                LocalBuilder existingVar = null)
            {
                var valVar = existingVar ?? il.DeclareLocal(exprType);
                il.Emit(OpCodes.Ldloca, valVar);
                il.Emit(OpCodes.Initobj, exprType);
                return valVar;
            }

            private static bool LoadClosureFieldOrItem(ref ClosureInfo closure, ILGenerator il, int itemIndex,
                Type itemType, Expression itemExprObj = null)
            {
                il.Emit(OpCodes.Ldarg_0); // closure is always a first argument

                var closureFields = closure.ClosureFields;
                if (closureFields != null)
                {
                    il.Emit(OpCodes.Ldfld, closureFields[itemIndex]);
                }
                else
                {
                    // for ArrayClosure load an array field
                    il.Emit(OpCodes.Ldfld, ArrayClosure.ArrayField);

                    // load array item index
                    EmitLoadConstantInt(il, itemIndex);

                    // load item from index
                    il.Emit(OpCodes.Ldelem_Ref);
                    itemType = itemType ?? itemExprObj?.Type;
                    if (itemType == null)
                    {
                        return false;
                    }

                    il.Emit(itemType.IsValueType() ? OpCodes.Unbox_Any : OpCodes.Castclass, itemType);
                }

                return true;
            }

            // todo: Replace resultValueVar with a closureInfo block
            private static bool TryEmitNew(ConstructorInfo ctor, Type exprType, ILGenerator il, LocalBuilder resultValueVar = null)
            {
                if (ctor != null)
                {
                    il.Emit(OpCodes.Newobj, ctor);
                }
                else
                {
                    if (!exprType.IsValueType())
                    {
                        return false; // null constructor and not a value type, better fallback
                    }

                    var valueVar = InitValueTypeVariable(il, exprType, resultValueVar);
                    if (resultValueVar == null)
                    {
                        il.Emit(OpCodes.Ldloc, valueVar);
                    }
                }

                return true;
            }

            private static bool EmitNewArray(NewArrayExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                var arrayType = expr.Type;
                var elems = expr.Expressions;
                var elemType = arrayType.GetElementType();
                if (elemType == null)
                {
                    return false;
                }

                var arrVar = il.DeclareLocal(arrayType);

                var rank = arrayType.GetArrayRank();
                if (rank == 1) // one dimensional
                {
                    EmitLoadConstantInt(il, elems.Count);
                }
                else // multi dimensional
                {
                    for (var i = 0; i < elems.Count; i++)
                    {
                        if (!TryEmit(elems[i], paramExprs, il, ref closure, parent, i))
                        {
                            return false;
                        }
                    }

                    il.Emit(OpCodes.Newobj, arrayType.GetPublicInstanceConstructor());
                    return true;
                }

                il.Emit(OpCodes.Newarr, elemType);
                il.Emit(OpCodes.Stloc, arrVar);

                var isElemOfValueType = elemType.IsValueType();

                for (int i = 0, n = elems.Count; i < n; i++)
                {
                    il.Emit(OpCodes.Ldloc, arrVar);
                    EmitLoadConstantInt(il, i);

                    // loading element address for later copying of value into it.
                    if (isElemOfValueType)
                    {
                        il.Emit(OpCodes.Ldelema, elemType);
                    }

                    if (!TryEmit(elems[i], paramExprs, il, ref closure, parent))
                    {
                        return false;
                    }

                    if (isElemOfValueType)
                    {
                        il.Emit(OpCodes.Stobj, elemType); // store element of value type by array element address
                    }
                    else
                    {
                        il.Emit(OpCodes.Stelem_Ref);
                    }
                }

                il.Emit(OpCodes.Ldloc, arrVar);
                return true;
            }

            private static bool TryEmitArrayIndex(Type exprType, ILGenerator il)
            {
                if (exprType.IsValueType())
                {
                    il.Emit(OpCodes.Ldelem, exprType);
                }
                else
                {
                    il.Emit(OpCodes.Ldelem_Ref);
                }

                return true;
            }

            private static bool EmitMemberInit(MemberInitExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                // todo: Use closureInfo Block to track the variable instead
                LocalBuilder valueVar = null;
                if (expr.Type.IsValueType())
                {
                    valueVar = il.DeclareLocal(expr.Type);
                }

                var newExpr = expr.NewExpression;
#if LIGHT_EXPRESSION
                if (newExpr == null)
                {
                    if (!TryEmit(expr.Expression, paramExprs, il, ref closure, parent/*, valueVar*/)) // todo: fix me
                        return false;
                }
                else
#endif
                {
                    var argExprs = newExpr.Arguments;
                    for (var i = 0; i < argExprs.Count; i++)
                    {
                        if (!TryEmit(argExprs[i], paramExprs, il, ref closure, parent, i))
                        {
                            return false;
                        }
                    }

                    if (!TryEmitNew(newExpr.Constructor, newExpr.Type, il, valueVar))
                    {
                        return false;
                    }
                }

                var bindings = expr.Bindings;
                for (var i = 0; i < bindings.Count; i++)
                {
                    var binding = bindings[i];
                    if (binding.BindingType != MemberBindingType.Assignment)
                    {
                        return false;
                    }

                    if (valueVar != null) // load local value address, to set its members
                    {
                        il.Emit(OpCodes.Ldloca, valueVar);
                    }
                    else
                    {
                        il.Emit(OpCodes.Dup); // duplicate member owner on stack
                    }

                    if (!TryEmit(((MemberAssignment)binding).Expression, paramExprs, il, ref closure, parent) ||
                        !EmitMemberAssign(il, binding.Member))
                    {
                        return false;
                    }
                }

                if (valueVar != null)
                {
                    il.Emit(OpCodes.Ldloc, valueVar);
                }

                return true;
            }

            private static bool EmitMemberAssign(ILGenerator il, MemberInfo member)
            {
                switch (member)
                {
                    case PropertyInfo prop:
                        return EmitMethodCall(il, prop.GetSetter());

                    case FieldInfo field:
                        il.Emit(OpCodes.Stfld, field);
                        return true;
                }

                return false;
            }

            private static bool TryEmitIncDecAssign(UnaryExpression expr, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                var varIdx = closure.CurrentBlock.VarExprs.GetFirstIndex((ParameterExpression)expr.Operand);
                if (varIdx == -1)
                {
                    return false;
                }

                il.Emit(OpCodes.Ldloc, closure.CurrentBlock.LocalVars[varIdx]);

                var nodeType = expr.NodeType;
                if (nodeType == ExpressionType.PreIncrementAssign)
                {
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Add);
                    if ((parent & ParentFlags.IgnoreResult) == 0)
                    {
                        il.Emit(OpCodes.Dup);
                    }
                }
                else if (nodeType == ExpressionType.PostIncrementAssign)
                {
                    if ((parent & ParentFlags.IgnoreResult) == 0)
                    {
                        il.Emit(OpCodes.Dup);
                    }

                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Add);
                }
                else if (nodeType == ExpressionType.PreDecrementAssign)
                {
                    il.Emit(OpCodes.Ldc_I4_M1);
                    il.Emit(OpCodes.Add);
                    if ((parent & ParentFlags.IgnoreResult) == 0)
                    {
                        il.Emit(OpCodes.Dup);
                    }
                }
                else if (nodeType == ExpressionType.PostDecrementAssign)
                {
                    if ((parent & ParentFlags.IgnoreResult) == 0)
                    {
                        il.Emit(OpCodes.Dup);
                    }

                    il.Emit(OpCodes.Ldc_I4_M1);
                    il.Emit(OpCodes.Add);
                }

                il.Emit(OpCodes.Stloc, closure.CurrentBlock.LocalVars[varIdx]);
                return true;
            }

            private static bool TryEmitAssign(BinaryExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                var exprType = expr.Type;
                var left = expr.Left;
                var right = expr.Right;
                var leftNodeType = expr.Left.NodeType;
                var nodeType = expr.NodeType;

                // if this assignment is part of a single body-less expression or the result of a block
                // we should put its result to the evaluation stack before the return, otherwise we are
                // somewhere inside the block, so we shouldn't return with the result
                var flags = parent & ~ParentFlags.IgnoreResult;
                switch (leftNodeType)
                {
                    case ExpressionType.Parameter:
                        var leftParamExpr = (ParameterExpression)left;
                        var paramIndex = paramExprs.GetFirstIndex(leftParamExpr);

                        if (paramIndex != -1)
                        {
                            if (closure.HasClosure)
                            {
                                paramIndex +=
                                    1; // shift parameter indices by one, because the first one will be closure
                            }

                            if (paramIndex >= byte.MaxValue)
                            {
                                return false;
                            }

                            if (leftParamExpr.IsByRef)
                            {
                                EmitLoadParamArg(il, paramIndex, false);

                                var arithmeticNodeType = Tools.GetArithmeticFromArithmeticAssignOrSelf(nodeType);
                                if (arithmeticNodeType == nodeType)
                                {
                                    if (!TryEmit(right, paramExprs, il, ref closure, flags))
                                    {
                                        return false;
                                    }
                                }
                                else if (
                                    !TryEmit(expr.Left, paramExprs, il, ref closure, flags | ParentFlags.Arithmetic) ||
                                    !TryEmit(expr.Right, paramExprs, il, ref closure, flags | ParentFlags.Arithmetic) ||
                                    !TryEmitArithmeticOperation(arithmeticNodeType, exprType, il))
                                {
                                    return false;
                                }

                                EmitByRefStore(il, leftParamExpr.Type);
                            }
                            else
                            {
                                if (!TryEmit(right, paramExprs, il, ref closure, flags))
                                {
                                    return false;
                                }

                                if ((parent & ParentFlags.IgnoreResult) == 0)
                                {
                                    il.Emit(OpCodes.Dup); // dup value to assign and return
                                }

                                il.Emit(OpCodes.Starg_S, paramIndex);
                            }

                            return true;
                        }
                        else
                        {
                            var arithmeticNodeType = Tools.GetArithmeticFromArithmeticAssignOrSelf(nodeType);
                            if (arithmeticNodeType != nodeType)
                            {
                                var varIdx = closure.CurrentBlock.VarExprs.GetFirstIndex(leftParamExpr);
                                if (varIdx != -1)
                                {
                                    if (!TryEmit(expr.Left, paramExprs, il, ref closure, flags | ParentFlags.Arithmetic) ||
                                        !TryEmit(expr.Right, paramExprs, il, ref closure, flags | ParentFlags.Arithmetic) ||
                                        !TryEmitArithmeticOperation(arithmeticNodeType, exprType, il))
                                    {
                                        return false;
                                    }

                                    il.Emit(OpCodes.Stloc, closure.CurrentBlock.LocalVars[varIdx]);
                                    return true;
                                }
                            }
                        }

                        // if parameter isn't passed, then it is passed into some outer lambda or it is a local variable,
                        // so it should be loaded from closure or from the locals. Then the closure is null will be an invalid state.
                        if (!closure.IsClosureConstructed)
                        {
                            return false;
                        }

                        // if it's a local variable, then store the right value in it
                        var localVariable = closure.GetDefinedLocalVarOrDefault(leftParamExpr);
                        if (localVariable != null)
                        {
                            if (!TryEmit(right, paramExprs, il, ref closure, flags))
                            {
                                return false;
                            }

                            if ((right as ParameterExpression)?.IsByRef == true)
                            {
                                il.Emit(OpCodes.Ldind_I4);
                            }

                            if ((parent & ParentFlags.IgnoreResult) == 0) // if we have to push the result back, dup the right value
                            {
                                il.Emit(OpCodes.Dup);
                            }

                            il.Emit(OpCodes.Stloc, localVariable);
                            return true;
                        }

                        // check that it's a captured parameter by closure
                        var nonPassedParamIndex = closure.NonPassedParameters.GetFirstIndex(leftParamExpr);
                        if (nonPassedParamIndex == -1)
                        {
                            return false; // what??? no chance
                        }

                        var paramInClosureIndex = closure.Constants.Length + nonPassedParamIndex;

                        il.Emit(OpCodes.Ldarg_0); // closure is always a first argument

                        if ((parent & ParentFlags.IgnoreResult) == 0)
                        {
                            if (!TryEmit(right, paramExprs, il, ref closure, flags))
                            {
                                return false;
                            }

                            var valueVar = il.DeclareLocal(exprType); // store left value in variable
                            if (closure.ClosureFields != null)
                            {
                                il.Emit(OpCodes.Dup);
                                il.Emit(OpCodes.Stloc, valueVar);
                                il.Emit(OpCodes.Stfld, closure.ClosureFields[paramInClosureIndex]);
                                il.Emit(OpCodes.Ldloc, valueVar);
                            }
                            else
                            {
                                il.Emit(OpCodes.Stloc, valueVar);
                                il.Emit(OpCodes.Ldfld, ArrayClosure.ArrayField); // load array field
                                EmitLoadConstantInt(il, paramInClosureIndex); // load array item index
                                il.Emit(OpCodes.Ldloc, valueVar);
                                if (exprType.IsValueType())
                                {
                                    il.Emit(OpCodes.Box, exprType);
                                }

                                il.Emit(OpCodes.Stelem_Ref); // put the variable into array
                                il.Emit(OpCodes.Ldloc, valueVar);
                            }
                        }
                        else
                        {
                            var isArrayClosure = closure.ClosureFields == null;
                            if (isArrayClosure)
                            {
                                il.Emit(OpCodes.Ldfld, ArrayClosure.ArrayField); // load array field
                                EmitLoadConstantInt(il, paramInClosureIndex); // load array item index
                            }

                            if (!TryEmit(right, paramExprs, il, ref closure, flags))
                            {
                                return false;
                            }

                            if (isArrayClosure)
                            {
                                if (exprType.IsValueType())
                                {
                                    il.Emit(OpCodes.Box, exprType);
                                }

                                il.Emit(OpCodes.Stelem_Ref); // put the variable into array
                            }
                            else
                            {
                                il.Emit(OpCodes.Stfld, closure.ClosureFields[paramInClosureIndex]);
                            }
                        }

                        return true;

                    case ExpressionType.MemberAccess:
                        var memberExpr = (MemberExpression)left;
                        var member = memberExpr.Member;

                        var objExpr = memberExpr.Expression;
                        if (objExpr != null && !TryEmit(objExpr, paramExprs, il, ref closure, flags | ParentFlags.MemberAccess | ParentFlags.InstanceAccess) ||
                            !TryEmit(right, paramExprs, il, ref closure, ParentFlags.Empty))
                        {
                            return false;
                        }

                        if ((parent & ParentFlags.IgnoreResult) != 0)
                        {
                            return EmitMemberAssign(il, member);
                        }

                        il.Emit(OpCodes.Dup);

                        var rightVar = il.DeclareLocal(exprType); // store right value in variable
                        il.Emit(OpCodes.Stloc, rightVar);

                        if (!EmitMemberAssign(il, member))
                        {
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, rightVar);
                        return true;

                    case ExpressionType.Index:
                        var indexExpr = (IndexExpression)left;

                        var obj = indexExpr.Object;
                        if (obj != null && !TryEmit(obj, paramExprs, il, ref closure, flags))
                        {
                            return false;
                        }

                        var indexArgExprs = indexExpr.Arguments;
                        for (var i = 0; i < indexArgExprs.Count; i++)
                        {
                            if (!TryEmit(indexArgExprs[i], paramExprs, il, ref closure, flags, i))
                            {
                                return false;
                            }
                        }

                        if (!TryEmit(right, paramExprs, il, ref closure, flags))
                        {
                            return false;
                        }

                        if ((parent & ParentFlags.IgnoreResult) != 0)
                        {
                            return TryEmitIndexAssign(indexExpr, obj?.Type, exprType, il);
                        }

                        var variable = il.DeclareLocal(exprType); // store value in variable to return
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Stloc, variable);

                        if (!TryEmitIndexAssign(indexExpr, obj?.Type, exprType, il))
                        {
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, variable);
                        return true;

                    default: // todo: not yet support assignment targets
                        return false;
                }
            }

            private static void EmitByRefStore(ILGenerator il, Type type)
            {
                if (type == typeof(int) || type == typeof(uint))
                {
                    il.Emit(OpCodes.Stind_I4);
                }
                else if (type == typeof(byte))
                {
                    il.Emit(OpCodes.Stind_I1);
                }
                else if (type == typeof(short) || type == typeof(ushort))
                {
                    il.Emit(OpCodes.Stind_I2);
                }
                else if (type == typeof(long) || type == typeof(ulong))
                {
                    il.Emit(OpCodes.Stind_I8);
                }
                else if (type == typeof(float))
                {
                    il.Emit(OpCodes.Stind_R4);
                }
                else if (type == typeof(double))
                {
                    il.Emit(OpCodes.Stind_R8);
                }
                else if (type == typeof(object))
                {
                    il.Emit(OpCodes.Stind_Ref);
                }
                else if (type == typeof(IntPtr) || type == typeof(UIntPtr))
                {
                    il.Emit(OpCodes.Stind_I);
                }
                else
                {
                    il.Emit(OpCodes.Stobj, type);
                }
            }

            private static bool TryEmitIndexAssign(IndexExpression indexExpr, Type instType, Type elementType, ILGenerator il)
            {
                if (indexExpr.Indexer != null)
                {
                    return EmitMemberAssign(il, indexExpr.Indexer);
                }

                if (indexExpr.Arguments.Count == 1) // one dimensional array
                {
                    if (elementType.IsValueType())
                    {
                        il.Emit(OpCodes.Stelem, elementType);
                    }
                    else
                    {
                        il.Emit(OpCodes.Stelem_Ref);
                    }

                    return true;
                }

                // multi dimensional array
                return EmitMethodCall(il, instType?.GetPublicInstanceMethod("Set"));
            }

            private static bool TryEmitMethodCall(MethodCallExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                var objExpr = expr.Object;
                var callFlags = parent & ~ParentFlags.IgnoreResult | ParentFlags.Call;
                if (objExpr != null)
                {
                    if (!TryEmit(objExpr, paramExprs, il, ref closure, callFlags | ParentFlags.InstanceAccess))
                    {
                        return false;
                    }

                    if (objExpr.Type.IsValueType() && objExpr.NodeType != ExpressionType.Parameter && !closure.LastEmitIsAddress)
                    {
                        var theVar = il.DeclareLocal(objExpr.Type);
                        il.Emit(OpCodes.Stloc, theVar);
                        il.Emit(OpCodes.Ldloca, theVar);
                    }
                }

                IList<Expression> argExprs = expr.Arguments;
                if (argExprs.Count != 0)
                {
                    var args = expr.Method.GetParameters();
                    for (var i = 0; i < argExprs.Count; i++)
                    {
                        var byRefIndex = args[i].ParameterType.IsByRef ? i : -1;
                        if (!TryEmit(argExprs[i], paramExprs, il, ref closure, callFlags,
                            byRefIndex))
                        {
                            return false;
                        }
                    }
                }

                if (expr.Method.IsVirtual && objExpr?.Type.IsValueType() == true)
                {
                    il.Emit(OpCodes.Constrained, objExpr.Type);
                }

                closure.LastEmitIsAddress = false;

                return EmitMethodCall(il, expr.Method, parent);
            }

            private static bool TryEmitMemberAccess(MemberExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                var prop = expr.Member as PropertyInfo;
                var instanceExpr = expr.Expression;
                if (instanceExpr != null &&
                    !TryEmit(instanceExpr, paramExprs, il, ref closure,
                        parent | (prop != null ? ParentFlags.Call : parent) | ParentFlags.MemberAccess | ParentFlags.InstanceAccess))
                {
                    return false;
                }

                if (prop != null)
                {
                    // Value type special treatment to load address of value instance in order to access a field or call a method.
                    // Parameter should be excluded because it already loads an address via Ldarga, and you don't need to.
                    // And for field access no need to load address, cause the field stored on stack nearby
                    if (instanceExpr != null && !closure.LastEmitIsAddress && instanceExpr.NodeType != ExpressionType.Parameter && instanceExpr.Type.IsValueType())
                    {
                        var theVar = il.DeclareLocal(instanceExpr.Type);
                        il.Emit(OpCodes.Stloc, theVar);
                        il.Emit(OpCodes.Ldloca, theVar);
                    }

                    closure.LastEmitIsAddress = false;
                    return EmitMethodCall(il, prop.GetGetter());
                }

                var field = expr.Member as FieldInfo;
                if (field != null)
                {
                    if (field.IsStatic)
                    {
                        if (field.IsLiteral)
                        {
                            var value = field.GetValue(null);
                            TryEmitConstant(null, field.FieldType, value, il, ref closure);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldsfld, field);
                        }
                    }
                    else
                    {
                        closure.LastEmitIsAddress = (field.FieldType.IsValueType() && (parent & ParentFlags.InstanceAccess) > 0);
                        il.Emit(closure.LastEmitIsAddress ? OpCodes.Ldflda : OpCodes.Ldfld, field);
                    }
                    return true;
                }

                return false;
            }

            // ReSharper disable once FunctionComplexityOverflow
            private static bool TryEmitNestedLambda(LambdaExpression lambdaExpr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure)
            {
                // First, find in closed compiled lambdas the one corresponding to the current lambda expression.
                // Situation with not found lambda is not possible/exceptional,
                // it means that we somehow skipped the lambda expression while collecting closure info.
                var outerNestedLambdaExprs = closure.NestedLambdaExprs;
                var outerNestedLambdaIndex = outerNestedLambdaExprs.GetFirstIndex(lambdaExpr);
                if (outerNestedLambdaIndex == -1)
                {
                    return false;
                }

                var nestedLambdaInfo = closure.NestedLambdas[outerNestedLambdaIndex];
                var nestedLambda = nestedLambdaInfo.Lambda;

                var outerConstants = closure.Constants;
                var outerNonPassedParams = closure.NonPassedParameters;

                // Load compiled lambda on stack counting the offset
                outerNestedLambdaIndex += outerConstants.Length + outerNonPassedParams.Length;

                if (!LoadClosureFieldOrItem(ref closure, il, outerNestedLambdaIndex, nestedLambda.GetType()))
                {
                    return false;
                }

                // If lambda does not use any outer parameters to be set in closure, then we're done
                var nestedClosureInfo = nestedLambdaInfo.ClosureInfo;
                if (!nestedClosureInfo.HasClosure)
                {
                    return true;
                }

                // If closure is array-based, the create a new array to represent closure for the nested lambda
                var isNestedArrayClosure = nestedClosureInfo.ClosureFields == null;
                if (isNestedArrayClosure)
                {
                    EmitLoadConstantInt(il, nestedClosureInfo.ClosedItemCount); // size of array
                    il.Emit(OpCodes.Newarr, typeof(object));
                }

                // Load constants on stack
                var nestedConstants = nestedClosureInfo.Constants;
                if (nestedConstants.Length != 0)
                {
                    for (var nestedConstIndex = 0; nestedConstIndex < nestedConstants.Length; nestedConstIndex++)
                    {
                        var nestedConstant = nestedConstants[nestedConstIndex];

                        // Find constant index in the outer closure
                        var outerConstIndex = outerConstants.GetFirstIndex(nestedConstant);
                        if (outerConstIndex == -1)
                        {
                            return false; // some error is here
                        }

                        if (isNestedArrayClosure)
                        {
                            // Duplicate nested array on stack to store the item, and load index to where to store
                            il.Emit(OpCodes.Dup);
                            EmitLoadConstantInt(il, nestedConstIndex);
                        }

                        if (!LoadClosureFieldOrItem(ref closure, il, outerConstIndex, nestedConstant.Type))
                        {
                            return false;
                        }

                        if (isNestedArrayClosure)
                        {
                            if (nestedConstant.Type.IsValueType())
                            {
                                il.Emit(OpCodes.Box, nestedConstant.Type);
                            }

                            il.Emit(OpCodes.Stelem_Ref); // store the item in array
                        }
                    }
                }

                // Load used and closed parameter values on stack
                var nestedNonPassedParams = nestedClosureInfo.NonPassedParameters;
                for (var nestedParamIndex = 0; nestedParamIndex < nestedNonPassedParams.Length; nestedParamIndex++)
                {
                    var nestedUsedParam = nestedNonPassedParams[nestedParamIndex];

                    Type nestedUsedParamType = null;
                    if (isNestedArrayClosure)
                    {
                        // get a param type for the later
                        nestedUsedParamType = nestedUsedParam.Type;

                        // Duplicate nested array on stack to store the item, and load index to where to store
                        il.Emit(OpCodes.Dup);
                        EmitLoadConstantInt(il, nestedConstants.Length + nestedParamIndex);
                    }

                    var paramIndex = paramExprs.GetFirstIndex(nestedUsedParam);
                    if (paramIndex != -1) // load param from input params
                    {
                        // +1 is set cause of added first closure argument
                        EmitLoadParamArg(il, 1 + paramIndex, false);
                    }
                    else // load parameter from outer closure or from the locals
                    {
                        if (outerNonPassedParams.Length == 0)
                        {
                            return false; // impossible, better to throw?
                        }

                        var variable = closure.GetDefinedLocalVarOrDefault(nestedUsedParam);
                        if (variable != null) // it's a local variable
                        {
                            il.Emit(OpCodes.Ldloc, variable);
                        }
                        else // it's a parameter from outer closure
                        {
                            var outerParamIndex = outerNonPassedParams.GetFirstIndex(nestedUsedParam);
                            if (outerParamIndex == -1 ||
                                !LoadClosureFieldOrItem(ref closure, il, outerConstants.Length + outerParamIndex,
                                    nestedUsedParamType, nestedUsedParam))
                            {
                                return false;
                            }
                        }
                    }

                    if (isNestedArrayClosure)
                    {
                        if (nestedUsedParamType.IsValueType())
                        {
                            il.Emit(OpCodes.Box, nestedUsedParamType);
                        }

                        il.Emit(OpCodes.Stelem_Ref); // store the item in array
                    }
                }

                // Load nested lambdas on stack
                var nestedLambdaExprs = closure.NestedLambdaExprs;
                var nestedNestedLambdaExprs = nestedClosureInfo.NestedLambdaExprs;
                var nestedNestedLambdas = nestedClosureInfo.NestedLambdas;
                if (nestedNestedLambdas.Length != 0)
                {
                    for (var nestedLambdaIndex = 0; nestedLambdaIndex < nestedNestedLambdas.Length; nestedLambdaIndex++)
                    {
                        var nestedNestedLambda = nestedNestedLambdas[nestedLambdaIndex];

                        // Find constant index in the outer closure
                        var outerLambdaIndex =
                            nestedLambdaExprs.GetFirstIndex(nestedNestedLambdaExprs[nestedLambdaIndex]);
                        if (outerLambdaIndex == -1)
                        {
                            return false; // some error is here
                        }

                        // Duplicate nested array on stack to store the item, and load index to where to store
                        if (isNestedArrayClosure)
                        {
                            il.Emit(OpCodes.Dup);
                            EmitLoadConstantInt(il, nestedConstants.Length + nestedNonPassedParams.Length + nestedLambdaIndex);
                        }

                        outerLambdaIndex += outerConstants.Length + outerNonPassedParams.Length;

                        var nestedNestedLambdaType = nestedNestedLambda.Lambda.GetType();
                        if (!LoadClosureFieldOrItem(ref closure, il, outerLambdaIndex, nestedNestedLambdaType))
                        {
                            return false;
                        }

                        if (isNestedArrayClosure)
                        {
                            il.Emit(OpCodes.Stelem_Ref); // store the item in array
                        }
                    }
                }

                // Create nested closure object composed of all constants, params, lambdas loaded on stack
                il.Emit(
                    OpCodes.Newobj,
                    isNestedArrayClosure
                        ? ArrayClosure.Constructor
                        : nestedClosureInfo.ClosureType.GetPublicInstanceConstructors().First());

                return EmitMethodCall(il, GetCurryClosureMethod(nestedLambda, nestedLambdaInfo.IsAction));
            }

            private static MethodInfo GetCurryClosureMethod(object lambda, bool isAction)
            {
                var lambdaTypeArgs = lambda.GetType().GetGenericTypeArguments();
                return isAction
                    ? CurryClosureActions.Methods[lambdaTypeArgs.Length - 1].MakeGenericMethod(lambdaTypeArgs)
                    : CurryClosureFuncs.Methods[lambdaTypeArgs.Length - 2].MakeGenericMethod(lambdaTypeArgs);
            }

            private static bool TryEmitInvoke(InvocationExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                // optimization #138: we are inlining invoked lambda body (only for lambdas without arguments) 
                var lambda = expr.Expression;
                if (lambda is LambdaExpression lambdaExpr && lambdaExpr.Parameters.Count == 0)
                {
                    return TryEmit(lambdaExpr.Body, paramExprs, il, ref closure, parent);
                }

                if (!TryEmit(lambda, paramExprs, il, ref closure, parent & ~ParentFlags.IgnoreResult))
                {
                    return false;
                }

                var argExprs = expr.Arguments;
                for (var i = 0; i < argExprs.Count; i++)
                {
                    var byRefIndex = argExprs[i].Type.IsByRef ? i : -1;
                    if (!TryEmit(argExprs[i], paramExprs, il, ref closure, parent & ~ParentFlags.IgnoreResult, byRefIndex))
                    {
                        return false;
                    }
                }

                return EmitMethodCall(il, lambda.Type.FindDelegateInvokeMethod(), parent);
            }

            private static bool TryEmitSwitch(SwitchExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                // todo:
                //- use switch statement for int comparison (if int difference is less or equal 3 -> use il switch)
                //- TryEmitComparison should not emit "ceq" so we could use Beq_S instead of Brtrue_S (not always possible (nullables))
                //- if switch SwitchValue is a nullable parameter, we should call getValue only once and store the result.
                //- use comparison methods (when defined)

                var endLabel = il.DefineLabel();
                var labels = new Label[expr.Cases.Count];
                for (var index = 0; index < expr.Cases.Count; index++)
                {
                    var switchCase = expr.Cases[index];
                    labels[index] = il.DefineLabel();

                    foreach (var switchCaseTestValue in switchCase.TestValues)
                    {
                        if (!TryEmitComparison(expr.SwitchValue, switchCaseTestValue, ExpressionType.Equal, paramExprs, il,
                            ref closure, parent))
                        {
                            return false;
                        }

                        il.Emit(OpCodes.Brtrue, labels[index]);
                    }
                }

                if (expr.DefaultBody != null)
                {
                    if (!TryEmit(expr.DefaultBody, paramExprs, il, ref closure, parent))
                    {
                        return false;
                    }

                    il.Emit(OpCodes.Br, endLabel);
                }

                for (var index = 0; index < expr.Cases.Count; index++)
                {
                    var switchCase = expr.Cases[index];
                    il.MarkLabel(labels[index]);
                    if (!TryEmit(switchCase.Body, paramExprs, il, ref closure, parent))
                    {
                        return false;
                    }

                    if (index != expr.Cases.Count - 1)
                    {
                        il.Emit(OpCodes.Br, endLabel);
                    }
                }

                il.MarkLabel(endLabel);

                return true;
            }

            private static bool TryEmitComparison(Expression exprLeft, Expression exprRight, ExpressionType expressionType,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                // todo: for now, handling only parameters of the same type
                // todo: for now, Nullable is not supported
                var leftOpType = exprLeft.Type;
                var leftIsNull = leftOpType.IsNullable();
                var rightOpType = exprRight.Type;
                if (exprRight is ConstantExpression c && c.Value == null && exprRight.Type == typeof(object))
                {
                    rightOpType = leftOpType;
                }

                LocalBuilder lVar = null, rVar = null;
                if (!TryEmit(exprLeft, paramExprs, il, ref closure, parent & ~ParentFlags.IgnoreResult & ~ParentFlags.InstanceAccess))
                {
                    return false;
                }

                if (leftIsNull)
                {
                    lVar = il.DeclareLocal(leftOpType);
                    il.Emit(OpCodes.Stloc_S, lVar);
                    il.Emit(OpCodes.Ldloca_S, lVar);

                    if (!EmitMethodCall(il, leftOpType.FindNullableValueOrDefaultMethod()))
                    {
                        return false;
                    }

                    leftOpType = Nullable.GetUnderlyingType(leftOpType);
                }

                if (!TryEmit(exprRight, paramExprs, il, ref closure, parent & ~ParentFlags.IgnoreResult & ~ParentFlags.InstanceAccess))
                {
                    return false;
                }

                if (leftOpType != rightOpType)
                {
                    if (leftOpType.IsClass() && rightOpType.IsClass() && (leftOpType == typeof(object) || rightOpType == typeof(object)))
                    {
                        if (expressionType == ExpressionType.Equal)
                        {
                            il.Emit(OpCodes.Ceq);
                            if ((parent & ParentFlags.IgnoreResult) > 0)
                            {
                                il.Emit(OpCodes.Pop);
                            }
                        }
                        else if (expressionType == ExpressionType.NotEqual)
                        {
                            il.Emit(OpCodes.Ceq);
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                        }
                        else
                        {
                            return false;
                        }

                        if ((parent & ParentFlags.IgnoreResult) > 0)
                        {
                            il.Emit(OpCodes.Pop);
                        }

                        return true;
                    }
                }

                if (rightOpType.IsNullable())
                {
                    rVar = il.DeclareLocal(rightOpType);
                    il.Emit(OpCodes.Stloc_S, rVar);
                    il.Emit(OpCodes.Ldloca_S, rVar);
                    if (!EmitMethodCall(il, rightOpType.FindNullableValueOrDefaultMethod()))
                    {
                        return false;
                    }
                }

                if (!leftOpType.IsPrimitive() && !leftOpType.IsEnum())
                {
                    var methodName
                        = expressionType == ExpressionType.Equal ? "op_Equality"
                        : expressionType == ExpressionType.NotEqual ? "op_Inequality"
                        : expressionType == ExpressionType.GreaterThan ? "op_GreaterThan"
                        : expressionType == ExpressionType.GreaterThanOrEqual ? "op_GreaterThanOrEqual"
                        : expressionType == ExpressionType.LessThan ? "op_LessThan"
                        : expressionType == ExpressionType.LessThanOrEqual ? "op_LessThanOrEqual"
                        : null;

                    if (methodName == null)
                    {
                        return false;
                    }

                    // todo: for now handling only parameters of the same type
                    var method = leftOpType
                        .GetPublicStaticMethods(methodName)
                        .GetFirst(m => m.GetParameters().All(p => p.ParameterType == leftOpType));

                    if (method != null)
                    {
                        return EmitMethodCall(il, method);
                    }

                    if (expressionType != ExpressionType.Equal && expressionType != ExpressionType.NotEqual)
                    {
                        return false;
                    }

                    EmitMethodCall(il, _objectEqualsMethod);
                    if (expressionType == ExpressionType.NotEqual) // invert result for not equal
                    {
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                    }

                    if (leftIsNull)
                    {
                        goto nullCheck;
                    }

                    if ((parent & ParentFlags.IgnoreResult) > 0)
                    {
                        il.Emit(OpCodes.Pop);
                    }

                    return true;
                }

                // handle primitives comparison
                switch (expressionType)
                {
                    case ExpressionType.Equal:
                        il.Emit(OpCodes.Ceq);
                        break;

                    case ExpressionType.NotEqual:
                        il.Emit(OpCodes.Ceq);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                        break;

                    case ExpressionType.LessThan:
                        il.Emit(OpCodes.Clt);
                        break;

                    case ExpressionType.GreaterThan:
                        il.Emit(OpCodes.Cgt);
                        break;

                    case ExpressionType.LessThanOrEqual:
                        il.Emit(OpCodes.Cgt);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                        break;

                    case ExpressionType.GreaterThanOrEqual:
                        il.Emit(OpCodes.Clt);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                        break;

                    default:
                        return false;
                }

                nullCheck:
                if (leftIsNull)
                {
                    il.Emit(OpCodes.Ldloca_S, lVar);
                    var hasValueMethod = exprLeft.Type.FindNullableHasValueGetterMethod();
                    if (!EmitMethodCall(il, hasValueMethod))
                    {
                        return false;
                    }
                    // ReSharper disable once AssignNullToNotNullAttribute
                    il.Emit(OpCodes.Ldloca_S, rVar);
                    if (!EmitMethodCall(il, hasValueMethod))
                    {
                        return false;
                    }

                    switch (expressionType)
                    {
                        case ExpressionType.Equal:
                            il.Emit(OpCodes.Ceq); // compare both HasValue calls
                            il.Emit(OpCodes.And); // both results need to be true
                            break;

                        case ExpressionType.NotEqual:
                            il.Emit(OpCodes.Ceq);
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                            il.Emit(OpCodes.Or);
                            break;

                        case ExpressionType.LessThan:
                        case ExpressionType.GreaterThan:
                        case ExpressionType.LessThanOrEqual:
                        case ExpressionType.GreaterThanOrEqual:
                            il.Emit(OpCodes.Ceq);
                            il.Emit(OpCodes.Ldc_I4_1);
                            il.Emit(OpCodes.Ceq);
                            il.Emit(OpCodes.And);
                            break;

                        default:
                            return false;
                    }
                }

                if ((parent & ParentFlags.IgnoreResult) > 0)
                {
                    il.Emit(OpCodes.Pop);
                }

                return true;
            }

            private static bool TryEmitArithmeticOperation(ExpressionType exprNodeType, Type exprType, ILGenerator il)
            {
                if (!exprType.IsPrimitive())
                {
                    if (exprType.IsNullable())
                    {
                        exprType = Nullable.GetUnderlyingType(exprType);
                    }

                    if (!exprType.IsPrimitive())
                    {
                        MethodInfo method = null;
                        if (exprType == typeof(string))
                        {
                            method = StringExpressionExtensions.GetConcatMethod(parameterCount: 2);
                        }
                        else
                        {
                            var methodName
                                = exprNodeType == ExpressionType.Add ? "op_Addition"
                                : exprNodeType == ExpressionType.AddChecked ? "op_Addition"
                                : exprNodeType == ExpressionType.Subtract ? "op_Subtraction"
                                : exprNodeType == ExpressionType.SubtractChecked ? "op_Subtraction"
                                : exprNodeType == ExpressionType.Multiply ? "op_Multiply"
                                : exprNodeType == ExpressionType.MultiplyChecked ? "op_Multiply"
                                : exprNodeType == ExpressionType.Divide ? "op_Division"
                                : exprNodeType == ExpressionType.Modulo ? "op_Modulus"
                                : null;

                            if (methodName != null)
                            {
                                method = exprType.GetPublicStaticMethod(methodName);
                            }
                        }
                        return method != null && EmitMethodCall(il, method);
                    }
                }

                switch (exprNodeType)
                {
                    case ExpressionType.Add:
                    case ExpressionType.AddAssign:
                        il.Emit(OpCodes.Add);
                        return true;

                    case ExpressionType.AddChecked:
                    case ExpressionType.AddAssignChecked:
                        il.Emit(IsUnsigned(exprType) ? OpCodes.Add_Ovf_Un : OpCodes.Add_Ovf);
                        return true;

                    case ExpressionType.Subtract:
                    case ExpressionType.SubtractAssign:
                        il.Emit(OpCodes.Sub);
                        return true;

                    case ExpressionType.SubtractChecked:
                    case ExpressionType.SubtractAssignChecked:
                        il.Emit(IsUnsigned(exprType) ? OpCodes.Sub_Ovf_Un : OpCodes.Sub_Ovf);
                        return true;

                    case ExpressionType.Multiply:
                    case ExpressionType.MultiplyAssign:
                        il.Emit(OpCodes.Mul);
                        return true;

                    case ExpressionType.MultiplyChecked:
                    case ExpressionType.MultiplyAssignChecked:
                        il.Emit(IsUnsigned(exprType) ? OpCodes.Mul_Ovf_Un : OpCodes.Mul_Ovf);
                        return true;

                    case ExpressionType.Divide:
                    case ExpressionType.DivideAssign:
                        il.Emit(OpCodes.Div);
                        return true;

                    case ExpressionType.Modulo:
                    case ExpressionType.ModuloAssign:
                        il.Emit(OpCodes.Rem);
                        return true;

                    case ExpressionType.And:
                    case ExpressionType.AndAssign:
                        il.Emit(OpCodes.And);
                        return true;

                    case ExpressionType.Or:
                    case ExpressionType.OrAssign:
                        il.Emit(OpCodes.Or);
                        return true;

                    case ExpressionType.ExclusiveOr:
                    case ExpressionType.ExclusiveOrAssign:
                        il.Emit(OpCodes.Xor);
                        return true;

                    case ExpressionType.LeftShift:
                    case ExpressionType.LeftShiftAssign:
                        il.Emit(OpCodes.Shl);
                        return true;

                    case ExpressionType.RightShift:
                    case ExpressionType.RightShiftAssign:
                        il.Emit(OpCodes.Shr);
                        return true;

                    case ExpressionType.Power:
                        return EmitMethodCall(il, typeof(Math).GetPublicStaticMethod("Pow"));
                }

                return false;
            }

            private static bool IsUnsigned(Type type)
            {
                return type == typeof(byte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong);
            }

            private static bool TryEmitLogicalOperator(BinaryExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                if (!TryEmit(expr.Left, paramExprs, il, ref closure, parent))
                {
                    return false;
                }

                var labelSkipRight = il.DefineLabel();
                il.Emit(expr.NodeType == ExpressionType.AndAlso ? OpCodes.Brfalse : OpCodes.Brtrue, labelSkipRight);

                if (!TryEmit(expr.Right, paramExprs, il, ref closure, parent))
                {
                    return false;
                }

                var labelDone = il.DefineLabel();
                il.Emit(OpCodes.Br, labelDone);

                il.MarkLabel(labelSkipRight); // label the second branch
                il.Emit(expr.NodeType == ExpressionType.AndAlso ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
                il.MarkLabel(labelDone);

                return true;
            }

            private static bool TryEmitConditional(ConditionalExpression expr,
                IList<ParameterExpression> paramExprs, ILGenerator il, ref ClosureInfo closure, ParentFlags parent)
            {
                var testExpr = expr.Test;
                var usedInverted = false;

                // optimization: special handling of comparing with null
                if (testExpr is BinaryExpression b &&
                    ((testExpr.NodeType == ExpressionType.Equal || testExpr.NodeType == ExpressionType.NotEqual) &&
                     !(b.Left.Type.IsNullable() || b.Right.Type.IsNullable()) &&
                      b.Right is ConstantExpression r && r.Value == null
                    ? TryEmit(b.Left, paramExprs, il, ref closure, parent & ~ParentFlags.IgnoreResult)
                    : b.Left is ConstantExpression l && l.Value == null &&
                      TryEmit(b.Right, paramExprs, il, ref closure, parent & ~ParentFlags.IgnoreResult)))
                {
                    usedInverted = true;
                }
                else if (!TryEmit(testExpr, paramExprs, il, ref closure, parent & ~ParentFlags.IgnoreResult))
                {
                    return false;
                }

                var labelIfFalse = il.DefineLabel();
                il.Emit(usedInverted && testExpr.NodeType == ExpressionType.Equal ? OpCodes.Brtrue : OpCodes.Brfalse, labelIfFalse);

                var ifTrueExpr = expr.IfTrue;
                if (!TryEmit(ifTrueExpr, paramExprs, il, ref closure, parent))
                {
                    return false;
                }

                var labelDone = il.DefineLabel();
                var ifFalseExpr = expr.IfFalse;

                il.Emit(OpCodes.Br, labelDone);

                il.MarkLabel(labelIfFalse);
                if (!TryEmit(ifFalseExpr, paramExprs, il, ref closure, parent))
                {
                    return false;
                }

                il.MarkLabel(labelDone);
                return true;
            }

            private static bool EmitMethodCall(ILGenerator il, MethodInfo method, ParentFlags parent = ParentFlags.Empty)
            {
                if (method == null)
                {
                    return false;
                }

                il.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);

                if ((parent & ParentFlags.IgnoreResult) != 0 && method.ReturnType != typeof(void))
                {
                    il.Emit(OpCodes.Pop);
                }

                return true;
            }

            private static void EmitLoadConstantInt(ILGenerator il, int i)
            {
                switch (i)
                {
                    case -1:
                        il.Emit(OpCodes.Ldc_I4_M1);
                        break;
                    case 0:
                        il.Emit(OpCodes.Ldc_I4_0);
                        break;
                    case 1:
                        il.Emit(OpCodes.Ldc_I4_1);
                        break;
                    case 2:
                        il.Emit(OpCodes.Ldc_I4_2);
                        break;
                    case 3:
                        il.Emit(OpCodes.Ldc_I4_3);
                        break;
                    case 4:
                        il.Emit(OpCodes.Ldc_I4_4);
                        break;
                    case 5:
                        il.Emit(OpCodes.Ldc_I4_5);
                        break;
                    case 6:
                        il.Emit(OpCodes.Ldc_I4_6);
                        break;
                    case 7:
                        il.Emit(OpCodes.Ldc_I4_7);
                        break;
                    case 8:
                        il.Emit(OpCodes.Ldc_I4_8);
                        break;
                    case int n when (n > -129 && n < 128):
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)i);
                        break;
                    default:
                        il.Emit(OpCodes.Ldc_I4, i);
                        break;
                }
            }
        }
    }

    // Helpers targeting the performance. Extensions method names may be a bit funny (non standard), 
    // in order to prevent conflicts with YOUR helpers with standard names
    internal static class Tools
    {
        internal static bool IsNullable(this Type type) => type.IsClosedTypeOf(typeof(Nullable<>));

        internal static MethodInfo FindDelegateInvokeMethod(this Type type) =>
            type.GetPublicInstanceMethod("Invoke");

        internal static MethodInfo FindNullableValueOrDefaultMethod(this Type type) =>
            type.GetPublicInstanceMethod("GetValueOrDefault", parameterCount: 0);

        internal static MethodInfo FindNullableHasValueGetterMethod(this Type type) =>
            type.GetPublicInstanceMethod("get_HasValue");

        // todo: test what is faster? Copy and inline switch? Switch in method? Ors in method?

        internal static ExpressionType GetArithmeticFromArithmeticAssignOrSelf(ExpressionType arithmetic)
        {
            switch (arithmetic)
            {
                case ExpressionType.AddAssign: return ExpressionType.Add;
                case ExpressionType.AddAssignChecked: return ExpressionType.AddChecked;
                case ExpressionType.SubtractAssign: return ExpressionType.Subtract;
                case ExpressionType.SubtractAssignChecked: return ExpressionType.SubtractChecked;
                case ExpressionType.MultiplyAssign: return ExpressionType.Multiply;
                case ExpressionType.MultiplyAssignChecked: return ExpressionType.MultiplyChecked;
                case ExpressionType.DivideAssign: return ExpressionType.Divide;
                case ExpressionType.ModuloAssign: return ExpressionType.Modulo;
                case ExpressionType.PowerAssign: return ExpressionType.Power;
                case ExpressionType.AndAssign: return ExpressionType.And;
                case ExpressionType.OrAssign: return ExpressionType.Or;
                case ExpressionType.ExclusiveOrAssign: return ExpressionType.ExclusiveOr;
                case ExpressionType.LeftShiftAssign: return ExpressionType.LeftShift;
                case ExpressionType.RightShiftAssign: return ExpressionType.RightShift;
                default: return arithmetic;
            }
        }

        public static Type[] GetParamTypes(IList<ParameterExpression> paramExprs)
        {
            if (paramExprs == null || paramExprs.Count == 0)
            {
                return Constants.NoTypeArguments;
            }

            if (paramExprs.Count == 1)
            {
                return new[] { paramExprs[0].IsByRef ? paramExprs[0].Type.MakeByRefType() : paramExprs[0].Type };
            }

            var paramTypes = new Type[paramExprs.Count];
            for (var i = 0; i < paramTypes.Length; i++)
            {
                var parameterExpr = paramExprs[i];
                paramTypes[i] = parameterExpr.IsByRef ? parameterExpr.Type.MakeByRefType() : parameterExpr.Type;
            }

            return paramTypes;
        }

        public static Type GetFuncOrActionType(Type[] paramTypes, Type returnType)
        {
            if (returnType == typeof(void))
            {
                switch (paramTypes.Length)
                {
                    case 0: return typeof(Action);
                    case 1: return typeof(Action<>).MakeGenericType(paramTypes);
                    case 2: return typeof(Action<,>).MakeGenericType(paramTypes);
                    case 3: return typeof(Action<,,>).MakeGenericType(paramTypes);
                    case 4: return typeof(Action<,,,>).MakeGenericType(paramTypes);
                    case 5: return typeof(Action<,,,,>).MakeGenericType(paramTypes);
                    case 6: return typeof(Action<,,,,,>).MakeGenericType(paramTypes);
                    case 7: return typeof(Action<,,,,,,>).MakeGenericType(paramTypes);
                    default:
                        throw new NotSupportedException(
                            $"Action with so many ({paramTypes.Length}) parameters is not supported!");
                }
            }

            paramTypes = paramTypes.Append(returnType);
            switch (paramTypes.Length)
            {
                case 1: return typeof(Func<>).MakeGenericType(paramTypes);
                case 2: return typeof(Func<,>).MakeGenericType(paramTypes);
                case 3: return typeof(Func<,,>).MakeGenericType(paramTypes);
                case 4: return typeof(Func<,,,>).MakeGenericType(paramTypes);
                case 5: return typeof(Func<,,,,>).MakeGenericType(paramTypes);
                case 6: return typeof(Func<,,,,,>).MakeGenericType(paramTypes);
                case 7: return typeof(Func<,,,,,,>).MakeGenericType(paramTypes);
                case 8: return typeof(Func<,,,,,,,>).MakeGenericType(paramTypes);
                default:
                    throw new NotSupportedException(
                        $"Func with so many ({paramTypes.Length}) parameters is not supported!");
            }
        }

        public static int GetFirstIndex<TElement, TItem>(this IList<TElement> source, TItem item)
        {
            if (source == null || source.Count == 0)
            {
                return -1;
            }

            var count = source.Count;
            if (count == 1)
            {
                return ReferenceEquals(source[0], item) ? 0 : -1;
            }

            for (var i = 0; i < count; ++i)
            {
                if (ReferenceEquals(source[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        public static int GetFirstIndex<T>(this T[] source, Func<T, bool> predicate)
        {
            if (source == null || source.Length == 0)
            {
                return -1;
            }

            if (source.Length == 1)
            {
                return predicate(source[0]) ? 0 : -1;
            }

            for (var i = 0; i < source.Length; ++i)
            {
                if (predicate(source[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        public static T GetFirst<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            var arr = source as T[];
            if (arr == null)
            {
                return source.FirstOrDefault(predicate);
            }

            var index = arr.GetFirstIndex(predicate);
            return index == -1 ? default(T) : arr[index];
        }
#endif
    }
}