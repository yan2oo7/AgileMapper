namespace AgileObjects.AgileMapper.Members
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
#if NET35
    using Microsoft.Scripting.Ast;
    using static Microsoft.Scripting.Ast.ExpressionType;
#else
    using System.Linq.Expressions;
    using static System.Linq.Expressions.ExpressionType;
#endif
    using Extensions;
    using Extensions.Internal;
    using NetStandardPolyfills;
    using ReadableExpressions.Extensions;
    using static Member;

    internal class ExpressionInfoFinder
    {
        public static readonly ExpressionInfo EmptyExpressionInfo =
            new ExpressionInfo(null, Enumerable<Expression>.EmptyArray);

        public static ExpressionInfoFinder Default =>
            _default ?? (_default = new ExpressionInfoFinder(rootObjects: Enumerable<Expression>.EmptyArray));

        private static ExpressionInfoFinder _default;

        private readonly IList<Expression> _rootObjects;

        public ExpressionInfoFinder(IList<Expression> rootObjects)
        {
            _rootObjects = rootObjects;
        }

        public ExpressionInfo FindIn(
            Expression expression,
            bool targetCanBeNull = false,
            bool checkMultiInvocations = true,
            bool invertNestedAccessChecks = false)
        {
            var finder = new ExpressionInfoFinderInstance(
                _rootObjects,
                targetCanBeNull,
                checkMultiInvocations,
                invertNestedAccessChecks);

            var info = finder.FindIn(expression);

            return info;
        }

        private class ExpressionInfoFinderInstance : ExpressionVisitor
        {
            private readonly IList<Expression> _rootObjects;
            private readonly bool _includeTargetNullChecking;
            private readonly bool _checkMultiInvocations;
            private readonly bool _invertNestedAccessChecks;
            private ICollection<Expression> _stringMemberAccessSubjects;
            private ICollection<string> _nullCheckSubjects;
            private Dictionary<string, Expression> _nestedAccessesByPath;
            private ICollection<Expression> _allInvocations;
            private ICollection<Expression> _multiInvocations;

            public ExpressionInfoFinderInstance(
                IList<Expression> rootObjects,
                bool targetCanBeNull,
                bool checkMultiInvocations,
                bool invertNestedAccessChecks)
            {
                _rootObjects = rootObjects;
                _includeTargetNullChecking = targetCanBeNull;
                _checkMultiInvocations = checkMultiInvocations;
                _invertNestedAccessChecks = invertNestedAccessChecks;
            }

            private ICollection<Expression> StringMemberAccessSubjects
                => _stringMemberAccessSubjects ?? (_stringMemberAccessSubjects = new List<Expression>());

            private ICollection<string> NullCheckSubjects
                => _nullCheckSubjects ?? (_nullCheckSubjects = new List<string>());

            private Dictionary<string, Expression> NestedAccessesByPath
                => _nestedAccessesByPath ?? (_nestedAccessesByPath = new Dictionary<string, Expression>());

            private ICollection<Expression> AllInvocations
                => _allInvocations ?? (_allInvocations = new List<Expression>());

            private ICollection<Expression> MultiInvocations
                => _multiInvocations ?? (_multiInvocations = new List<Expression>());

            public ExpressionInfo FindIn(Expression expression)
            {
                Visit(expression);

                if ((_nestedAccessesByPath == null) && (_multiInvocations == null))
                {
                    return EmptyExpressionInfo;
                }

                var nestedAccessChecks = GetNestedAccessChecks();

                var multiInvocations = _checkMultiInvocations && _multiInvocations?.Any() == true
                    ? _multiInvocations.OrderBy(inv => inv.ToString()).ToArray()
                    : Enumerable<Expression>.EmptyArray;

                return new ExpressionInfo(nestedAccessChecks, multiInvocations);
            }

            private Expression GetNestedAccessChecks()
            {
                if (_nestedAccessesByPath == null)
                {
                    return null;
                }

                var nestedAccessCount = _nestedAccessesByPath.Count;

                if (nestedAccessCount == 1)
                {
                    return GetAccessCheck(_nestedAccessesByPath.Values.First());
                }

                var nestedAccessCheckChain = default(Expression);

                foreach (var nestedAccessCheck in _nestedAccessesByPath.Values.Reverse().Project(GetAccessCheck))
                {
                    if (nestedAccessCheckChain == null)
                    {
                        nestedAccessCheckChain = nestedAccessCheck;
                        continue;
                    }

                    nestedAccessCheckChain = _invertNestedAccessChecks
                        ? Expression.OrElse(nestedAccessCheckChain, nestedAccessCheck)
                        : Expression.AndAlso(nestedAccessCheckChain, nestedAccessCheck);
                }

                return nestedAccessCheckChain;
            }

            private Expression GetAccessCheck(Expression access)
            {
                switch (access.NodeType)
                {
                    case ArrayIndex:
                        var arrayIndexAccess = (BinaryExpression)access;
                        var arrayLength = Expression.ArrayLength(arrayIndexAccess.Left);
                        var arrayIndexValue = arrayIndexAccess.Right;

                        return _invertNestedAccessChecks
                            ? Expression.Equal(arrayLength, arrayIndexValue)
                            : Expression.GreaterThan(arrayLength, arrayIndexValue);

                    case Index:
                        var index = (IndexExpression)access;
                        var indexKeyType = index.Indexer.GetGetter().GetParameters().First().ParameterType;

                        if (!indexKeyType.IsNumeric())
                        {
                            goto default;
                        }

                        var count = index.Object.GetCount();

                        if (count == null)
                        {
                            goto default;
                        }

                        var indexValue = index.Arguments.First().GetConversionTo(count.Type);

                        return _invertNestedAccessChecks
                            ? Expression.LessThanOrEqual(count, indexValue)
                            : Expression.GreaterThan(count, indexValue);

                    default:
                        return _invertNestedAccessChecks
                            ? access.GetIsDefaultComparison()
                            : access.GetIsNotDefaultComparison();
                }
            }

            protected override Expression VisitBinary(BinaryExpression binary)
            {
                if (TryGetNullComparison(binary.Left, binary.Right, binary, out var comparedValue) ||
                    TryGetNullComparison(binary.Right, binary.Left, binary, out comparedValue))
                {
                    AddExistingNullCheck(comparedValue);

                    return base.VisitBinary(binary);
                }

                if (IsRelevantArrayIndexAccess(binary))
                {
                    AddMemberAccess(binary);
                }

                return base.VisitBinary(binary);
            }

            private bool TryGetNullComparison(
                Expression comparedOperand,
                Expression nullOperand,
                Expression binary,
                out Expression comparedValue)
            {
                if ((binary.NodeType != Equal) && (binary.NodeType != NotEqual))
                {
                    comparedValue = null;
                    return false;
                }

                if ((nullOperand.NodeType != Constant) || (((ConstantExpression)nullOperand).Value != null))
                {
                    comparedValue = null;
                    return false;
                }

                comparedValue = GuardMemberAccess(comparedOperand) ? comparedOperand : null;
                return comparedValue != null;
            }

            private static bool IsRelevantArrayIndexAccess(BinaryExpression binary)
            {
                return (binary.NodeType == ArrayIndex) &&
                       (binary.Right.NodeType != Parameter);
            }

            protected override Expression VisitMember(MemberExpression memberAccess)
            {
                if ((memberAccess.Expression == null) || IsRootObject(memberAccess))
                {
                    return base.VisitMember(memberAccess);
                }

                if (memberAccess.IsNullableHasValueAccess())
                {
                    AddExistingNullCheck(memberAccess.Expression);
                }

                AddStringMemberAccessSubjectIfAppropriate(memberAccess.Expression);
                AddMemberAccessIfAppropriate(memberAccess);

                return base.VisitMember(memberAccess);
            }

            private bool IsNotRootObject(MemberExpression memberAccess) => !IsRootObject(memberAccess);

            private bool IsRootObject(MemberExpression memberAccess)
            {
                switch (memberAccess.Member.Name)
                {
                    case nameof(IMappingData.Parent):
                    case RootSourceMemberName:
                    case nameof(IMappingData<int, int>.EnumerableIndex):
                        return IsMappingDataMember(memberAccess);

                    case RootTargetMemberName:
                        return _includeTargetNullChecking && IsMappingDataMember(memberAccess);

                    default:
                        return _rootObjects.Contains(memberAccess.Expression);
                }
            }

            private static bool IsMappingDataMember(MemberExpression memberAccess)
            {
                // ReSharper disable once PossibleNullReferenceException
                return memberAccess.Member.DeclaringType.Name
                    .StartsWith(nameof(IMappingData), StringComparison.Ordinal);
            }

            protected override Expression VisitIndex(IndexExpression indexAccess)
            {
                if ((indexAccess.Object.Type != typeof(string)) &&
                    !indexAccess.Object.Type.IsDictionary() &&
                     IndexDoesNotUseParameter(indexAccess.Arguments[0]))
                {
                    AddMemberAccess(indexAccess);
                }

                return base.VisitIndex(indexAccess);
            }

            private static bool IndexDoesNotUseParameter(Expression indexExpression)
            {
                if (indexExpression == null)
                {
                    return true;
                }

                switch (indexExpression.NodeType)
                {
                    case Call:
                        var methodCall = (MethodCallExpression)indexExpression;

                        return IndexDoesNotUseParameter(methodCall.Object) &&
                               methodCall.Arguments.All(IndexDoesNotUseParameter);

                    case Parameter:
                        return false;
                }

                return true;
            }

            protected override Expression VisitMethodCall(MethodCallExpression methodCall)
            {
                if ((methodCall.Method.DeclaringType == typeof(IMappingData)) ||
                    _rootObjects.Contains(methodCall.Object))
                {
                    return base.VisitMethodCall(methodCall);
                }

                if (IsNullableGetValueOrDefaultCall(methodCall))
                {
                    AddExistingNullCheck(methodCall.Object);
                }

                AddStringMemberAccessSubjectIfAppropriate(methodCall.Object);
                AddInvocationIfNecessary(methodCall);
                AddMemberAccessIfAppropriate(methodCall);

                return base.VisitMethodCall(methodCall);
            }

            private static bool IsNullableGetValueOrDefaultCall(MethodCallExpression methodCall)
            {
                return (methodCall.Object != null) &&
                       (methodCall.Method.Name == nameof(Nullable<int>.GetValueOrDefault)) &&
                       (methodCall.Object.Type.IsNullableType());
            }

            private void AddExistingNullCheck(Expression checkedAccess)
            {
                NullCheckSubjects.Add(checkedAccess.ToString());
            }

            private void AddStringMemberAccessSubjectIfAppropriate(Expression member)
            {
                if ((member?.Type == typeof(string)) && AccessSubjectCouldBeNull(member))
                {
                    StringMemberAccessSubjects.Add(member);
                }
            }

            private bool AccessSubjectCouldBeNull(Expression expression)
            {
                Expression subject;
                MemberExpression memberAccess;

                if (expression.NodeType == MemberAccess)
                {
                    memberAccess = (MemberExpression)expression;
                    subject = memberAccess.Expression;
                }
                else
                {
                    memberAccess = null;
                    subject = ((MethodCallExpression)expression).Object;
                }

                if (subject == null)
                {
                    return false;
                }

                if (subject.Type.CannotBeNull())
                {
                    return false;
                }

                return (expression.NodeType != MemberAccess) || IsNotRootObject(memberAccess);
            }

            protected override Expression VisitInvocation(InvocationExpression invocation)
            {
                AddInvocationIfNecessary(invocation);

                return base.VisitInvocation(invocation);
            }

            private void AddInvocationIfNecessary(Expression invocation)
            {
                if (!_checkMultiInvocations)
                {
                    return;
                }

                if (_allInvocations?.Contains(invocation) != true)
                {
                    AllInvocations.Add(invocation);
                }
                else if (_multiInvocations?.Contains(invocation) != true)
                {
                    MultiInvocations.Add(invocation);
                }
            }

            private void AddMemberAccessIfAppropriate(Expression memberAccess)
            {
                if (GuardMemberAccess(memberAccess))
                {
                    AddMemberAccess(memberAccess);
                }
            }

            private bool GuardMemberAccess(Expression memberAccess)
            {
                if (IsNonNullReturnMethodCall(memberAccess))
                {
                    return false;
                }

                if (memberAccess.Type.CannotBeNull() ||
                  (_rootObjects?.Any(memberAccess, (ma, ro) => ma.IsRootedIn(ro)) == false))
                {
                    return false;
                }

                if ((memberAccess.Type == typeof(string)) &&
                   (_stringMemberAccessSubjects?.Contains(memberAccess) != true))
                {
                    return false;
                }

                return true;
            }

            private static bool IsNonNullReturnMethodCall(Expression memberAccess)
            {
                if (memberAccess.NodeType != Call)
                {
                    return false;
                }

                var method = ((MethodCallExpression)memberAccess).Method;

                switch (method.Name)
                {
                    case nameof(string.ToString) when method.DeclaringType == typeof(object):
                    case nameof(string.Split) when method.DeclaringType == typeof(string):
                    case nameof(IEnumerable<int>.GetEnumerator) when method.DeclaringType.IsClosedTypeOf(typeof(IEnumerable<>)):
                        return true;

                    case nameof(Enumerable.Select):
                    case nameof(Enumerable.SelectMany):
                    case nameof(PublicEnumerableExtensions.Project):
                    case nameof(PublicEnumerableExtensions.Filter):
                    case nameof(Enumerable.Where):
                    case nameof(Enumerable.OrderBy):
                    case nameof(Enumerable.OrderByDescending):
                    case nameof(Enumerable.ToList):
                    case nameof(Enumerable.ToArray):
                        return (method.DeclaringType == typeof(Enumerable)) ||
                               (method.DeclaringType == typeof(PublicEnumerableExtensions));

                    default:
                        return false;
                }
            }

            private void AddMemberAccess(Expression memberAccess)
            {
                var memberAccessString = memberAccess.ToString();

                if (_nullCheckSubjects?.Contains(memberAccessString) == true ||
                    _nestedAccessesByPath?.ContainsKey(memberAccessString) == true)
                {
                    return;
                }

                NestedAccessesByPath.Add(memberAccessString, memberAccess);
            }
        }

        public class ExpressionInfo
        {
            public ExpressionInfo(
                Expression nestedAccessChecks,
                IList<Expression> multiInvocations)
            {
                NestedAccessChecks = nestedAccessChecks;
                MultiInvocations = multiInvocations;
            }

            public Expression NestedAccessChecks { get; }

            public IList<Expression> MultiInvocations { get; }
        }
    }
}