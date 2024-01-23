// Copyright (C) Microsoft Corporation. All Rights Reserved.

using System;

namespace Gardener.Core.Packaging;

public readonly struct NugetPackage : IEquatable<NugetPackage>
{
    public NugetPackage(string packageName, bool allowPrerelease)
    {
        Name = packageName;
        AllowPrerelease = allowPrerelease;
    }

    public string Name { get; }

    public bool AllowPrerelease { get; }

    public override int GetHashCode()
    {
        HashCode hashCode = default;
        hashCode.Add(Name, StringComparer.OrdinalIgnoreCase);
        hashCode.Add(AllowPrerelease);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(NugetPackage left, NugetPackage right)
        => StringComparer.OrdinalIgnoreCase.Equals(left.Name, right.Name)
            && left.AllowPrerelease == right.AllowPrerelease;

    public static bool operator !=(NugetPackage left, NugetPackage right) => !(left == right);

    public bool Equals(NugetPackage other) => this == other;

    public override bool Equals(object? obj)
    {
        if (obj is NugetPackage other)
        {
            return Equals(this, other);
        }

        return false;
    }
}
