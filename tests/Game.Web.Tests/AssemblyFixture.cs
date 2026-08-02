// Все тесты этого проекта делят один и тот же физический App_Data на диске (GameSessionHost
// строит пути от AppContext.BaseDirectory, общего на весь процесс тестового раннера) — по умолчанию
// xUnit параллелит разные тестовые классы как отдельные коллекции, и с несколькими классами в этом
// проекте (AuthenticationTests, MaterialChainDiagramTests, FactoryChainDiagramTests,
// TeamPageFactoryChainTests, …) это давало настоящую гонку записи в journal.jsonl, а не только
// устаревание in-memory ссылки одного долгоживущего хоста, от которого защищает HardReset() перед
// изолированными тестами (см. doc-comments в AuthenticationTests.cs). HardReset() сам по себе
// не спасает от двух хостов, пишущих в одно и то же время из разных потоков — только от того, что
// один хост переживает чужой сброс. Поэтому весь проект выполняется строго последовательно.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
