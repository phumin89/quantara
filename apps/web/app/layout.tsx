import type { Metadata } from 'next';
import { RootDocument } from './RootDocument';
import './globals.css';

export const metadata: Metadata = {
  title: 'Quantara',
  description: 'Quantara AI Company',
};

export default RootDocument.render;
