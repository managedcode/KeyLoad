import { isolatedCells } from './isolated-contracts.mjs';

// This describes only the authenticated original control family, never current vector/CRUD scale qualification.
export const HISTORICAL_SOURCES = Object.freeze(['aa49aa93982866b85a80e771a749d9b968ffc9f2',
  '73aebfd3f72695357834599e813aba77b9e274ad', '77167cbca9efe8942aab869dd52ad5b0b6cc72a1',
  'ff0af70a279b6adce653bc5fe2527fef51f9de69']);
export const HISTORICAL_TARGETS = Object.freeze(['KeyLoad', 'PostgreSQL + pgvector', 'Qdrant', 'RabbitMQ',
  'Redis', 'Neo4j', 'MongoDB', 'OpenSearch', 'KurrentDB']);
export const historicalControlCells = sourceRevision => HISTORICAL_SOURCES.includes(sourceRevision)
  ? isolatedCells().filter(cell => HISTORICAL_TARGETS.includes(cell.target)) : null;
