namespace AgileObjects.AgileMapper.Members
{
    using System;

    internal class TypeKey
    {
        private readonly KeyType _keyType;

        private TypeKey(Type type, KeyType keyType)
        {
            Type = type;
            _keyType = keyType;
        }

        public static TypeKey ForSourceMembers(Type type) => new TypeKey(type, KeyType.SourceMembers);

        public static TypeKey ForTargetMembers(Type type) => new TypeKey(type, KeyType.TargetMembers);

        public static TypeKey ForTypeId(Type type) => new TypeKey(type, KeyType.TypeId);

        public Type Type { get; }

        public override bool Equals(object obj)
        {
            var otherKey = obj as TypeKey;

            if (otherKey == null)
            {
                return false;
            }

            return (_keyType == otherKey._keyType) && (Type == otherKey.Type);
        }

        public override int GetHashCode() => 0;

        private enum KeyType { SourceMembers, TargetMembers, TypeId }
    }
}