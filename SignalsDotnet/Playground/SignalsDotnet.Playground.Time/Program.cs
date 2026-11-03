using R3;
using SignalsDotnet;


var current = DateTime.Now;

var s = Signal
    .ComputedObservable(() => Signal.Now > current + TimeSpan.FromSeconds(1))
    .Subscribe(Console.WriteLine);

Console.ReadLine();
