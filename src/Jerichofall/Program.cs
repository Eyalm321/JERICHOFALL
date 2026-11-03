using Jerichofall.View;

try
{
    var options=GameOptions.Parse(args);
    using var game=new JerichoGame(options);
    game.Run();
    return 0;
}
catch(Exception error)
{
    Console.Error.WriteLine("JERICHOFALL: "+error);
    return 1;
}
