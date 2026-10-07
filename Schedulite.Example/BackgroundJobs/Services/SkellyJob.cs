using System.Text.Json;
using Schedulite.Abstractions;

namespace Schedulite.Example.BackgroundJobs.Services;

public sealed class SkellyJob(TimeProvider timeProvider) : IBackgroundJob
{
    public string JobName => "Skelly Printer";
    public string JobDescription => "Draws a skeleton, waits for 10 seconds to simulate work, then says bye.";

    public async Task ExecuteAsync(BackgroundJobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine(
$"""
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
*              ___           _,.---,---.,_                                    *
*              |         ,;~'             '~;,                                *
*              |       ,;                     ;,                              *
*     Frontal  |      ;                         ; ,--- Supraorbital Foramen   *
*      Bone    |     ,'                         /'                            *
*              |    ,;                        /' ;,                           *
*              |    ; ;      .           . <-'  ; |                           *
*              |__  | ;   ______       ______   ;<----- Coronal Suture        *
*             ___   |  '/~"     ~" . "~     "~\'  |                           *
*             |     |  ~  ,-~~~^~, | ,~^~~~-,  ~  |                           *
*   Maxilla,  |      |   |        {"}"}:{"{"}        | <------ Orbit              *
*  Nasal and  |      |   l       / | \       !   |                            *
*  Zygomatic  |      .~  (__,.--" .^. "--.,__)  ~.                            *
*    Bones    |      |    ----;' / | \ `;-<--------- Infraorbital Foramen     *
*             |__     \__.       \/^\/       .__/                             *
*                ___   V| \                 / |V <--- Mastoid Process         *
*                |      | |T~\___!___!___/~T| |                               *
*                |      | |`IIII_I_I_I_IIII'| |                               *
*       Mandible |      |  \,III I I I III,/  |                               *
*                |       \   `~~~~~~~~~~'    /                                *
*                |         \   .       . <-x---- Mental Foramen               *
*                |__         \.    ^    ./                                    *
*                              ^~~~^~~~^                                      *
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
{timeProvider.GetUtcNow():O}] Hi from the {nameof(SkellyJob)} background job! context: {JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true })}
"""
        );
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        Console.WriteLine($"[{timeProvider.GetUtcNow():O}] Bye from the {nameof(SkellyJob)} background job.");
    }
}
