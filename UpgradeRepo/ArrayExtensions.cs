namespace UpgradeRepo;

public static class ArrayExtensions
{
    public static bool EndsWith<T>(this T[] lhs, T[] rhs)
    {
        bool endsWith = false;

        if (lhs.Length >= rhs.Length)
        {
            endsWith = true;
            for (int i = 0; i < rhs.Length; i++)
            {
                if (!lhs[lhs.Length - 1 - i].Equals(rhs[rhs.Length - 1 - i]))
                {
                    endsWith = false;
                    break;
                }
            }
        }

        return endsWith;
    }
}