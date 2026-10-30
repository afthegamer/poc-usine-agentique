using System.Text;
using Usine.Cli;

Console.OutputEncoding = Encoding.UTF8;
using var arret = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    arret.Cancel();
};

return await Commandes.ExecuterAsync(args, arret.Token);
