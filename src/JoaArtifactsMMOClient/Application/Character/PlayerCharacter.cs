using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using Application.Jobs;
using OneOf;
using OneOf.Types;

namespace Application.Character;

public class PlayerCharacter
{
    public string Name { get; init; }

    private (int x, int y) Coordinates { get; } = (0, 0);

    private CharacterJobHandler _jobHandler;

    public PlayerCharacter(string name)
    {
        Name = name;
    }
}
