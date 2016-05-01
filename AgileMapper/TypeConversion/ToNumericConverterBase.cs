﻿namespace AgileObjects.AgileMapper.TypeConversion
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    using Extensions;

    internal abstract class ToNumericConverterBase : TryParseConverterBase
    {
        private static readonly Type[] _handledSourceTypes =
            Constants.NumericTypes
                .Concat(typeof(string))
                .Concat(Constants.NumericTypes.Select(t => typeof(Nullable<>).MakeGenericType(t)))
                .ToArray();

        protected ToNumericConverterBase(Type numericType)
            : base(numericType)
        {
        }

        public override bool CanConvert(Type sourceType)
        {
            return base.CanConvert(sourceType) ||
                   sourceType.IsEnum ||
                   _handledSourceTypes.Contains(sourceType);
        }

        public override Expression GetConversion(Expression sourceValue, Type targetType)
        {
            if (IsCoercible(sourceValue))
            {
                return Expression.Convert(sourceValue, targetType);
            }

            return (sourceValue.Type != typeof(string))
                ? GetCheckedNumericConversion(sourceValue, targetType)
                : base.GetConversion(sourceValue, targetType);
        }

        protected abstract bool IsCoercible(Expression sourceValue);

        private static Expression GetCheckedNumericConversion(Expression sourceValue, Type targetType)
        {
            var numericValueIsValid = GetNumericValueValidityCheck(sourceValue, targetType);
            var castSourceValue = Expression.Convert(sourceValue, targetType);
            var defaultTargetType = Expression.Default(targetType);
            var inRangeValueOrDefault = Expression.Condition(numericValueIsValid, castSourceValue, defaultTargetType);

            return inRangeValueOrDefault;
        }

        private static Expression GetNumericValueValidityCheck(Expression sourceValue, Type targetType)
        {
            var numericValueIsInRange = NumericValueIsInRangeComparison.For(sourceValue, targetType);

            if (sourceValue.Type.IsEnum || sourceValue.Type.IsWholeNumberNumeric())
            {
                return numericValueIsInRange;
            }

            var one = GetConstantValue(1, sourceValue);
            var sourceValueModuloOne = Expression.Modulo(sourceValue, one);
            var zero = GetConstantValue(0, sourceValue);
            var moduloOneEqualsZero = Expression.Equal(sourceValueModuloOne, zero);

            return Expression.AndAlso(numericValueIsInRange, moduloOneEqualsZero);
        }

        private static Expression GetConstantValue(int value, Expression sourceValue)
        {
            Expression constant = Expression.Constant(value);

            return (sourceValue.Type != typeof(int))
                ? Expression.Convert(constant, sourceValue.Type) : constant;
        }
    }
}