namespace AgileObjects.AgileMapper.DataSources
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using Extensions;
    using Members;
    using NetStandardPolyfills;

    internal class DictionaryEntryVariablePair
    {
        #region Cached MethodInfos

        private static readonly MethodInfo _linqFirstOrDefaultMethod = typeof(Enumerable)
            .GetPublicStaticMethods()
            .First(m => (m.Name == "FirstOrDefault") && (m.GetParameters().Length == 2))
            .MakeGenericMethod(typeof(string));

        private static readonly MethodInfo _enumerableNoneMethod = typeof(EnumerableExtensions)
            .GetPublicStaticMethods()
            .First(m => (m.Name == "None") && (m.GetParameters().Length == 2))
            .MakeGenericMethod(typeof(string));

        private static readonly MethodInfo _stringStartsWithMethod = typeof(string)
            .GetPublicInstanceMethods()
            .First(m => (m.Name == "StartsWith") && (m.GetParameters().Length == 2));

        #endregion

        private readonly string _targetMemberName;
        private ParameterExpression _key;
        private ParameterExpression _value;

        public DictionaryEntryVariablePair(DictionarySourceMember sourceMember, IMemberMapperData mapperData)
        {
            SourceMember = sourceMember;
            MapperData = mapperData;
            _targetMemberName = mapperData.TargetMember.Name.ToCamelCase();
            UseDirectValueAccess = mapperData.TargetMember.Type.IsAssignableFrom(sourceMember.EntryType);
            Variables = UseDirectValueAccess ? new[] { Key } : new[] { Key, Value };
        }

        public DictionarySourceMember SourceMember { get; }

        public IMemberMapperData MapperData { get; }

        public IEnumerable<ParameterExpression> Variables { get; }

        public ParameterExpression Key
            => _key ?? (_key = Expression.Variable(typeof(string), _targetMemberName + "Key"));

        public ParameterExpression Value
            => _value ?? (_value = Expression.Variable(SourceMember.EntryType, _targetMemberName.ToCamelCase()));

        public bool HasConstantTargetMemberKey
        {
            get
            {
                return TargetMemberKey.NodeType == ExpressionType.Constant ||
                       TargetMemberKey.NodeType == ExpressionType.Parameter;
            }
        }

        public Expression TargetMemberKey { get; private set; }

        public bool UseDirectValueAccess { get; }

        public Expression GetTargetMemberDictionaryEnumerableElementKey(Expression index, IMemberMapperData mapperData)
        {
            var keyParts = mapperData.GetTargetMemberDictionaryKeyParts();
            var elementKeyParts = MapperData.GetTargetMemberDictionaryElementKeyParts(index);

            foreach (var elementKeyPart in elementKeyParts)
            {
                keyParts.Add(elementKeyPart);
            }

            return TargetMemberKey = keyParts.GetStringConcatCall();
        }

        public Expression GetKeyNotFoundShortCircuit(Expression shortCircuitReturn)
        {
            var sourceValueKeyAssignment = GetMatchingKeyAssignment();
            var keyNotFound = Key.GetIsDefaultComparison();
            var ifKeyNotFoundShortCircuit = Expression.IfThen(keyNotFound, shortCircuitReturn);

            return Expression.Block(sourceValueKeyAssignment, ifKeyNotFoundShortCircuit);
        }

        public Expression GetMatchingKeyAssignment()
            => GetMatchingKeyAssignment(MapperData.GetTargetMemberDictionaryKey());

        public Expression GetMatchingKeyAssignment(Expression targetMemberKey)
        {
            TargetMemberKey = targetMemberKey;

            var firstMatchingKeyOrNull = GetKeyMatchingQuery(
                HasConstantTargetMemberKey ? targetMemberKey : Key,
                Expression.Equal,
                (keyParameter, targetKey) => keyParameter.GetCaseInsensitiveEquals(targetKey),
                _linqFirstOrDefaultMethod);

            var keyVariableAssignment = GetKeyAssignment(firstMatchingKeyOrNull);

            return keyVariableAssignment;
        }

        public Expression GetNonConstantKeyAssignment() => GetKeyAssignment(TargetMemberKey);

        public Expression GetKeyAssignment(Expression value) => Expression.Assign(Key, value);

        public Expression GetNoKeysWithMatchingStartQuery(Expression targetMemberKey)
        {
            TargetMemberKey = targetMemberKey;

            var noKeysStartWithTarget = GetKeyMatchingQuery(
                targetMemberKey,
                (keyParameter, targetKey) => GetKeyStartsWithCall(keyParameter, targetKey, StringComparison.Ordinal),
                (keyParameter, targetKey) => GetKeyStartsWithCall(keyParameter, targetKey, StringComparison.OrdinalIgnoreCase),
                _enumerableNoneMethod);

            return noKeysStartWithTarget;
        }

        private static Expression GetKeyStartsWithCall(
            Expression keyParameter,
            Expression targetKey,
            StringComparison comparison)
        {
            return Expression.Call(
                keyParameter,
                _stringStartsWithMethod,
                targetKey,
                comparison.ToConstantExpression());
        }

        private Expression GetKeyMatchingQuery(
            Expression targetMemberKey,
            Func<Expression, Expression, Expression> rootKeyMatcherFactory,
            Func<Expression, Expression, Expression> nestedKeyMatcherFactory,
            MethodInfo queryMethod)
        {
            var keyParameter = Expression.Parameter(typeof(string), "key");

            var keyMatcher = MapperData.IsRoot
                ? rootKeyMatcherFactory.Invoke(keyParameter, targetMemberKey)
                : nestedKeyMatcherFactory.Invoke(keyParameter, targetMemberKey);

            var keyMatchesLambda = Expression.Lambda<Func<string, bool>>(keyMatcher, keyParameter);

            var dictionaryKeys = Expression.Property(MapperData.SourceObject, "Keys");
            var keyMatchesQuery = Expression.Call(queryMethod, dictionaryKeys, keyMatchesLambda);

            return keyMatchesQuery;
        }

        public Expression GetEntryValueAssignment() => Expression.Assign(Value, GetEntryValueAccess());

        public Expression GetEntryValueAccess() => GetEntryValueAccess(Key);

        public Expression GetEntryValueAccess(Expression key)
            => MapperData.SourceObject.GetIndexAccess(key);
    }
}